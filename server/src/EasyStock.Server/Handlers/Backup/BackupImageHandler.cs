using System.Text.RegularExpressions;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.Enums;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Handlers.Images;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Handlers.Backup;

/// <summary>
/// Backup photos from the image store: the file in <c>{DATA_DIR}/images/ab/cd/{sha256}.jpg</c> plus its <c>image</c>
/// row, written with the request's DbContext (so the row is part of the import transaction).
/// </summary>
public sealed partial class BackupImageHandler(ImageHandler store, EasyStockDbContext db, TimeProvider time, IOptions<ServerOptions> options) : IBackupImageHandler
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

    public Task<StagedImage> StageAsync(string sha256, Stream content, CancellationToken ct) =>
        // Same checks as PUT /images: a photo that is not a JPEG, too large or not matching its hash is not stored.
        store.StageAsync(Checked(sha256), content, quotaHint: null, ct);

    public async Task<StoredPhotos> StoreAsync(IReadOnlyCollection<StagedImage> photos, CancellationToken ct)
    {
        var moved = new List<string>();
        if (photos.Count == 0) return new StoredPhotos(moved, 0);
        await ImageHandler.LockAsync(db, ct);
        var inserted = 0;
        try
        {
            foreach (var photo in photos)
            {
                var (status, isNew) = await ImageHandler.ReserveAsync(db, Checked(photo.Sha256), photo.Size, options.Value.ImageQuotaBytes, time, ct);
                if (status == ImageSaveStatus.QuotaExceeded)
                    throw new BackupException(BackupError.ImageQuotaExceeded, "The photos do not fit in the image quota");
                if (isNew) inserted++;
                if (!isNew && store.Exists(photo.Sha256)) continue;
                store.Promote(photo);
                moved.Add(photo.Sha256);
            }
        }
        catch
        {
            DeleteFiles(moved); // the transaction rolls the rows back
            throw;
        }
        return new StoredPhotos(moved, inserted);
    }

    public void DeleteFiles(IEnumerable<string> sha256s)
    {
        foreach (var sha in sha256s) store.DeleteFile(Checked(sha));
    }
}
