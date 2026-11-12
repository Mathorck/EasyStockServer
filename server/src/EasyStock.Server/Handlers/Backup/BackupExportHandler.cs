using System.Data;
using System.IO.Compression;
using System.Text.Json;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Handlers.Backup;

/// <summary>
/// Builds a backup archive of the whole server data: every row of the nine shared tables (soft-deleted ones included),
/// read in one REPEATABLE READ snapshot, and the photos the articles reference. The archive goes to a temporary file
/// first (zip needs the sizes), so nothing is held in memory but one table at a time. The server-only tables (users,
/// sessions, invites, ...) are never part of it. Movements keep <c>created_by</c> and <c>device_id</c>, as the app's
/// own export does (user ids, no names).
/// </summary>
public sealed class BackupExportHandler(
    EasyStockDbContext db,
    IBackupImageHandler images,
    TimeProvider time,
    IOptions<ServerOptions> options,
    ILogger<BackupExportHandler> logger)
{
    public async Task<ExportedBackup> ExportAsync(Guid userId, CancellationToken ct)
    {
        var tempDir = Path.Combine(options.Value.DataDir, "tmp");
        Directory.CreateDirectory(tempDir);
        var file = new FileStream(Path.Combine(tempDir, $"backup-{Guid.NewGuid():N}.zip"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            var (rows, photos) = await WriteAsync(file, ct);
            file.Position = 0;
            var now = time.GetUtcNow();
            BackupLogHelper.Exported(logger, userId, rows, photos);
            return new ExportedBackup(file, $"easystock-server-{now:yyyyMMdd-HHmm}.{BackupFormatHelper.FileExtension}", rows, photos);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private async Task<(int Rows, int Photos)> WriteAsync(Stream output, CancellationToken ct)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        // Photos first: an article whose photo the server does not hold is exported without one.
        var hashes = await db.Articles.AsNoTracking().Where(a => a.ImageSha256 != null).Select(a => a.ImageSha256!).Distinct().ToListAsync(ct);
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sha in hashes)
            if (await images.ExistsAsync(sha, ct)) available.Add(sha);
        if (available.Count < hashes.Count) BackupLogHelper.PhotosMissing(logger, hashes.Count - available.Count);

        var counts = new Dictionary<string, int>
        {
            ["category"] = await db.Categories.CountAsync(ct),
            ["attribute_definition"] = await db.AttributeDefinitions.CountAsync(ct),
            ["category_attribute"] = await db.CategoryAttributes.CountAsync(ct),
            ["location"] = await db.Locations.CountAsync(ct),
            ["article"] = await db.Articles.CountAsync(ct),
            ["item"] = await db.Items.CountAsync(ct),
            ["stock_movement"] = await db.StockMovements.CountAsync(ct),
            ["stock_review"] = await db.StockReviews.CountAsync(ct),
            ["stock_review_line"] = await db.StockReviewLines.CountAsync(ct),
            ["images"] = available.Count,
        };
        var manifest = new BackupManifest(
            BackupFormatHelper.Format, BackupFormatHelper.FormatVersion, CompatHelper.ServerVersion, BackupFormatHelper.SchemaVersion,
            time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture), counts);
        await WriteJsonAsync(zip, BackupFormatHelper.ManifestEntry, manifest, ct);

        await WriteJsonAsync(zip, BackupTable.Category.Entry(),
            (await db.Categories.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()), ct);
        await WriteJsonAsync(zip, BackupTable.AttributeDefinition.Entry(),
            (await db.AttributeDefinitions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()), ct);
        await WriteJsonAsync(zip, BackupTable.CategoryAttribute.Entry(),
            (await db.CategoryAttributes.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()), ct);
        await WriteJsonAsync(zip, BackupTable.Location.Entry(),
            (await db.Locations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()), ct);
        await WriteJsonAsync(zip, BackupTable.Article.Entry(),
            (await db.Articles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))
                .Select(x => x.ImageSha256 is not null && !available.Contains(x.ImageSha256) ? x.ToRow() with { ImagePath = null } : x.ToRow()), ct);
        await WriteJsonAsync(zip, BackupTable.Item.Entry(),
            (await db.Items.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()), ct);
        await WriteJsonAsync(zip, BackupTable.StockMovement.Entry(),
            (await db.StockMovements.AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()), ct);
        await WriteJsonAsync(zip, BackupTable.StockReview.Entry(),
            (await db.StockReviews.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()), ct);
        await WriteJsonAsync(zip, BackupTable.StockReviewLine.Entry(),
            (await db.StockReviewLines.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()), ct);
        await tx.RollbackAsync(ct); // read-only: nothing to commit

        var written = 0;
        foreach (var sha in available.Order(StringComparer.Ordinal))
        {
            await using var photo = await images.OpenReadAsync(sha, ct);
            if (photo is null) continue; // removed since the check
            // JPEG is already compressed
            var entry = zip.CreateEntry(BackupFormatHelper.ImagePathFor(sha), CompressionLevel.Fastest);
            await using var target = await entry.OpenAsync(ct);
            await photo.CopyToAsync(target, ct);
            written++;
        }
        return (counts.Where(c => c.Key != "images").Sum(c => c.Value), written);
    }

    private static async Task WriteJsonAsync<T>(ZipArchive zip, string name, T value, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var target = await entry.OpenAsync(ct);
        await JsonSerializer.SerializeAsync(target, value, BackupFormatHelper.Json, ct);
    }
}
