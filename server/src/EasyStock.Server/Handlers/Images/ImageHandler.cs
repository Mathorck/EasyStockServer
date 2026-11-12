using System.Security.Cryptography;
using EasyStock.Server.Configuration;
using EasyStock.Server.Models;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Handlers.Images;

/// <summary>Photos on disk: ${DATA_DIR}/images/ab/cd/&lt;sha256&gt;.jpg. Touches the filesystem only when called.</summary>
public sealed partial class ImageHandler(IOptions<ServerOptions> options, ILogger<ImageHandler> logger)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public string PathFor(string sha256) =>
        Path.Combine(options.Value.DataDir, "images", sha256[..2], sha256[2..4], sha256 + ".jpg");

    public bool Exists(string sha256) => File.Exists(PathFor(sha256));

    public FileStream OpenRead(string sha256) =>
        new(PathFor(sha256), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

    /// <summary>Streams the body to a temp file next to the target while hashing, then moves it into place atomically.</summary>
    public async Task<ImageSaveResult> SaveAsync(string sha256, Stream body, CancellationToken ct)
    {
        var finalPath = PathFor(sha256);
        var dir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Guid.NewGuid():N}.tmp");
        try
        {
            long total = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var head = new byte[3];
            var headLen = 0;
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await body.ReadAsync(buffer, ct)) > 0)
                {
                    if (total + n > MaxBytes) return new ImageSaveResult(ImageSaveStatus.TooLarge);
                    for (var i = 0; i < n && headLen < 3; i++) head[headLen++] = buffer[i];
                    total += n;
                    hash.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            if (headLen < 3 || head[0] != 0xFF || head[1] != 0xD8 || head[2] != 0xFF)
                return new ImageSaveResult(ImageSaveStatus.NotJpeg);
            if (!Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(sha256, StringComparison.Ordinal))
                return new ImageSaveResult(ImageSaveStatus.HashMismatch);

            File.Move(temp, finalPath, overwrite: true);
            LogStored(sha256, total);
            return new ImageSaveResult(ImageSaveStatus.Saved, total);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored image {Sha256} ({Size} bytes)")]
    private partial void LogStored(string sha256, long size);
}
