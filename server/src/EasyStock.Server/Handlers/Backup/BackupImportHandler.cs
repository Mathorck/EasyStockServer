using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Handlers.Backup;

/// <summary>
/// Imports a backup archive. Merge: the app's merge rules (see <see cref="BackupMergeHelper"/>) applied to the server data.
/// Replace: the shared data is wiped and replaced by the backup. Both run in one transaction under the sync lock, so
/// no sync push or pull sees a half-imported state, and either everything is imported or nothing is. Photos are stored
/// (by SHA-256, so harmlessly repeatable) before the transaction starts.
/// </summary>
public sealed class BackupImportHandler(
    EasyStockDbContext db,
    IBackupImageHandler images,
    IBackupImportHooksHandler hooks,
    ILogger<BackupImportHandler> logger)
{
    /// <summary>Server-only tables that hold references to the shared data and are emptied with it on a replace.</summary>
    private static readonly string[] DerivedTables = ["low_stock_alert", "low_stock_state", "attribute_alias"];

    public async Task<BackupImportResult> ImportAsync(Stream file, BackupImportMode mode, Guid userId, CancellationToken ct)
    {
        using var archive = await BackupArchive.OpenAsync(file, ct);
        BackupValidatorHelper.Validate(archive.Data);
        var incoming = archive.Data;

        // Photo path in the archive -> SHA-256 of its bytes (only the ones an article refers to). Hashing checks every
        // photo (JPEG, within the image store's size limit) before any is written: a photo that cannot be stored
        // refuses the whole backup, so no article is left pointing at a photo the server does not hold.
        var pathToSha = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in incoming.ImagePaths())
            if (await archive.HashPhotoAsync(path, ct) is { } sha) pathToSha[path] = sha;
        var stored = 0;
        foreach (var (path, sha) in pathToSha)
        {
            if (await images.ExistsAsync(sha, ct)) continue;
            await using var photo = await archive.OpenPhotoAsync(path, ct);
            if (photo is null) continue;
            if (!await images.WriteAsync(sha, photo, ct)) throw new BackupException(BackupError.Damaged, "A photo could not be stored");
            stored++;
        }

        try
        {
            var result = await ApplyAsync(incoming, pathToSha, mode, stored, ct);
            BackupLogHelper.Imported(logger, userId, ModeName(mode), result.Added.Values.Sum(), result.Updated.Values.Sum(), stored, result.Conflicts.Count);
            return result;
        }
        catch (DbUpdateException e)
        {
            BackupLogHelper.DatabaseRejected(logger, userId, (e.InnerException as Npgsql.PostgresException)?.SqlState);
            throw new BackupException(BackupError.Rejected, "The database rejected the backup");
        }
    }

    private async Task<BackupImportResult> ApplyAsync(
        BackupData incoming, Dictionary<string, string> pathToSha, BackupImportMode mode, int stored, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireSyncLockAsync(ct);

        var local = mode == BackupImportMode.Merge ? await ReadLocalAsync(ct) : new BackupData();
        // Rewrite the photo paths of the incoming articles to the canonical path of their hash; no photo, no path.
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, sha) in pathToSha) canonical[path] = sha;
        var localShas = await db.Articles.AsNoTracking().Where(a => a.ImageSha256 != null).Select(a => a.ImageSha256!).Distinct().ToListAsync(ct);
        foreach (var sha in localShas) canonical.TryAdd(BackupFormatHelper.ImagePathFor(sha), sha);
        var rewritten = incoming with
        {
            Articles = incoming.Articles
                .Select(a => a.ImagePath is { } p && canonical.TryGetValue(p, out var sha) ? a with { ImagePath = BackupFormatHelper.ImagePathFor(sha) } : a with { ImagePath = null })
                .ToList(),
        };
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sha in canonical.Values) hashes[BackupFormatHelper.ImagePathFor(sha)] = sha;

        BackupImportResult result;
        if (mode == BackupImportMode.Replace)
        {
            var data = rewritten.WithItemLocations();
            await TruncateAsync(ct);
            await InsertAsync(data, hashes, ct);
            result = new BackupImportResult(ModeName(mode), data.PerTable(), new Dictionary<string, int>(), stored, []);
        }
        else
        {
            var plan = BackupMergeHelper.Plan(local, rewritten);
            await InsertAsync(plan.Inserts, hashes, ct);
            await UpdateAsync(plan.Updates, hashes, ct);
            await FixItemLocationsAsync(plan.ItemLocationFixes, ct);
            result = new BackupImportResult(
                ModeName(mode), plan.Inserts.PerTable(), plan.Updates.PerTable(), stored,
                plan.Conflicts.Select(c => new BackupConflictInfo(c.Type.Code(), c.Table.Name(), c.RowId, c.Value, c.Resolved)).ToList());
        }

        await hooks.BeforeCommitAsync(mode, db, ct);
        await tx.CommitAsync(ct);
        try
        {
            await hooks.AfterCommitAsync(mode, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            BackupLogHelper.AfterCommitFailed(logger, e.GetType().Name); // the import itself is done
        }
        return result;
    }

    private async Task<BackupData> ReadLocalAsync(CancellationToken ct) => new()
    {
        Categories = (await db.Categories.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
        AttributeDefinitions = (await db.AttributeDefinitions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
        CategoryAttributes = (await db.CategoryAttributes.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
        Locations = (await db.Locations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
        Articles = (await db.Articles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
        Items = (await db.Items.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
        Movements = (await db.StockMovements.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
        Reviews = (await db.StockReviews.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
        ReviewLines = (await db.StockReviewLines.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToRow()).ToList(),
    };

    /// <summary>
    /// Empties the shared tables and what refers to them. A plain DELETE cannot work (RESTRICT foreign keys, the
    /// location self-reference), so this is a TRUNCATE of the whole group. Any table outside the group that refers to
    /// the shared data stops the import instead of being emptied by accident.
    /// </summary>
    private async Task TruncateAsync(CancellationToken ct)
    {
        var shared = BackupTablesHelper.All.Select(t => t.Name()).ToList();
        var derived = new List<string>();
        foreach (var t in DerivedTables)
            if (await db.Database.SqlQuery<bool>($"SELECT to_regclass({t}) IS NOT NULL AS \"Value\"").SingleAsync(ct)) derived.Add(t);
        var group = shared.Concat(derived).ToList();

        var referencing = await db.Database.SqlQueryRaw<string>(
            "SELECT DISTINCT c.conrelid::regclass::text AS \"Value\" FROM pg_constraint c " +
            "WHERE c.contype = 'f' AND c.confrelid = ANY (SELECT to_regclass(x) FROM unnest(@p0) AS x)",
            new Npgsql.NpgsqlParameter("p0", shared.ToArray())).ToListAsync(ct);
        var unexpected = referencing.Where(r => !group.Contains(r)).ToList();
        if (unexpected.Count > 0)
            throw new InvalidOperationException("Tables refer to the shared data and are not part of the replace: " + string.Join(", ", unexpected));

#pragma warning disable EF1002, EF1003 // table names are constants of this class, never input
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE " + string.Join(", ", group), ct);
#pragma warning restore EF1002, EF1003
        db.ChangeTracker.Clear();
    }

    private async Task InsertAsync(BackupData d, Dictionary<string, string> hashes, CancellationToken ct)
    {
        db.Categories.AddRange(d.Categories.Select(r => r.ToEntity()));
        db.AttributeDefinitions.AddRange(d.AttributeDefinitions.Select(r => r.ToEntity()));
        await db.SaveChangesAsync(ct);
        db.CategoryAttributes.AddRange(d.CategoryAttributes.Select(r => r.ToEntity()));
        foreach (var l in BackupLocationsHelper.ParentsFirst(d.Locations.ToList()))
        {
            db.Locations.Add(l.ToEntity());
            await db.SaveChangesAsync(ct); // one by one: a child can only be written after its parent
        }
        db.Articles.AddRange(d.Articles.Select(r => r.ToEntity(hashes)));
        await db.SaveChangesAsync(ct);
        db.Items.AddRange(d.Items.Select(r => r.ToEntity()));
        await db.SaveChangesAsync(ct);
        db.StockMovements.AddRange(d.Movements.Select(r => r.ToEntity()));
        db.StockReviews.AddRange(d.Reviews.Select(r => r.ToEntity()));
        await db.SaveChangesAsync(ct);
        db.StockReviewLines.AddRange(d.ReviewLines.Select(r => r.ToEntity()));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task UpdateAsync(BackupData d, Dictionary<string, string> hashes, CancellationToken ct)
    {
        db.Categories.UpdateRange(d.Categories.Select(r => r.ToEntity()));
        db.AttributeDefinitions.UpdateRange(d.AttributeDefinitions.Select(r => r.ToEntity()));
        db.CategoryAttributes.UpdateRange(d.CategoryAttributes.Select(r => r.ToEntity()));
        db.Locations.UpdateRange(d.Locations.Select(r => r.ToEntity()));
        db.Articles.UpdateRange(d.Articles.Select(r => r.ToEntity(hashes)));
        db.Items.UpdateRange(d.Items.Select(r => r.ToEntity()));
        db.StockReviews.UpdateRange(d.Reviews.Select(r => r.ToEntity()));
        db.StockReviewLines.UpdateRange(d.ReviewLines.Select(r => r.ToEntity()));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task FixItemLocationsAsync(IReadOnlyDictionary<string, string?> fixes, CancellationToken ct)
    {
        if (fixes.Count == 0) return;
        var ids = fixes.Keys.Select(BackupMappingHelper.ParseId).ToList();
        foreach (var item in await db.Items.Where(i => ids.Contains(i.Id)).ToListAsync(ct))
            item.LocationId = fixes[item.Id.ToString("D")] is { } l ? BackupMappingHelper.ParseId(l) : null;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static string ModeName(BackupImportMode mode) => mode == BackupImportMode.Merge ? "merge" : "replace";
}
