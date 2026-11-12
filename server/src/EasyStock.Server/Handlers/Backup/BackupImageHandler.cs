using System.Text.RegularExpressions;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Handlers.Images;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Backup;

/// <summary>
/// Backup photos from the image store: the file in <c>{DATA_DIR}/images/ab/cd/{sha256}.jpg</c> plus its <c>image</c>
/// row, written with the request's DbContext (so the row is part of the import transaction).
/// </summary>
public sealed partial class BackupImageHandler(ImageHandler store, EasyStockDbContext db, TimeProvider time) : IBackupImageHandler
{
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Format();

    private static string Checked(string sha256) =>
        // The hash becomes a file path: nothing else than 64 lower-case hex characters may get through.
        Sha256Format().IsMatch(sha256) ? sha256 : throw new ArgumentException("Not a SHA-256 hash.", nameof(sha256));

    public async Task<Stream?> OpenReadAsync(string sha256, CancellationToken ct) =>
        await ExistsAsync(sha256, ct) ? store.OpenRead(sha256) : null;

    public async Task<bool> ExistsAsync(string sha256, CancellationToken ct) =>
        store.Exists(Checked(sha256)) && await db.Set<ImageRecord>().AnyAsync(i => i.Sha256 == sha256, ct);

    public async Task WriteAsync(string sha256, Stream content, CancellationToken ct)
    {
        // Same checks as PUT /images: a photo that is not a JPEG, too large or not matching its hash is skipped.
        var result = await store.SaveAsync(Checked(sha256), content, ct);
        if (result.Status != ImageSaveStatus.Saved) return;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO image (sha256, size, created_at) VALUES ({sha256}, {result.Size}, {time.GetUtcNow().ToUnixTimeMilliseconds()}) ON CONFLICT (sha256) DO NOTHING", ct);
    }
}
