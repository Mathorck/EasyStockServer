using System.Security.Cryptography;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;
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
    /// <param name="quotaRemaining">Bytes still free under the storage cap; null for no check (the photo is already stored).</param>
    public async Task<ImageSaveResult> SaveAsync(string sha256, Stream body, long? quotaRemaining, CancellationToken ct)
    {
        var finalPath = PathFor(sha256);
        var dir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Guid.NewGuid():N}.tmp");
        try
        {
            long total = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var head = new byte[4];
            byte tail1 = 0, tail2 = 0; // the last two bytes seen
            var headLen = 0;
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await body.ReadAsync(buffer, ct)) > 0)
                {
                    if (total + n > MaxBytes) return new ImageSaveResult(ImageSaveStatus.TooLarge);
                    if (quotaRemaining is { } free && total + n > free) return new ImageSaveResult(ImageSaveStatus.QuotaExceeded);
                    for (var i = 0; i < n && headLen < head.Length; i++) head[headLen++] = buffer[i];
                    tail1 = n >= 2 ? buffer[n - 2] : tail2;
                    tail2 = buffer[n - 1];
                    total += n;
                    hash.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            if (!LooksLikeJpeg(head, headLen, tail1, tail2, total))
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

    /// <summary>
    /// Start of image (FF D8), then a marker (FF xx, xx not 00 or FF), and end of image (FF D9) as the last two bytes.
    /// This rejects renamed files and truncated uploads; it does not decode the picture.
    /// </summary>
    public static bool LooksLikeJpeg(byte[] head, int headLen, byte tail1, byte tail2, long total) =>
        headLen == 4 && total >= 6
        && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF && head[3] is not (0x00 or 0xFF)
        && tail1 == 0xFF && tail2 == 0xD9;

    /// <summary>Bytes used by the stored photos, as recorded in the `image` table.</summary>
    public static async Task<long> UsedBytesAsync(EasyStockDbContext db, CancellationToken ct) =>
        await db.Set<ImageRecord>().SumAsync(i => (long?)i.Size, ct) ?? 0;

    /// <summary>
    /// Deletes the photos no article references, if they are older than <paramref name="olderThan"/> (a photo is uploaded
    /// before the push that references it). Removes the file and the row; returns how many were deleted.
    /// For the cleanup job to call; nothing calls it yet.
    /// </summary>
    public async Task<int> DeleteOrphansAsync(EasyStockDbContext db, TimeSpan olderThan, TimeProvider time, CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().Add(-olderThan).ToUnixTimeMilliseconds();
        var orphans = await db.Set<ImageRecord>()
            .Where(i => i.CreatedAt < cutoff
                && !db.Set<Article>().Any(a => a.ImageSha256 == i.Sha256))
            .Select(i => i.Sha256).ToListAsync(ct);
        var deleted = 0;
        foreach (var sha in orphans)
        {
            // Re-check in the delete itself: a push may have referenced the photo since the list was read.
            var rows = await db.Set<ImageRecord>()
                .Where(i => i.Sha256 == sha && !db.Set<Article>().Any(a => a.ImageSha256 == sha))
                .ExecuteDeleteAsync(ct);
            if (rows == 0) continue;
            try { File.Delete(PathFor(sha)); } catch (IOException) { }
            deleted++;
        }
        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored image {Sha256} ({Size} bytes)")]
    private partial void LogStored(string sha256, long size);
}
