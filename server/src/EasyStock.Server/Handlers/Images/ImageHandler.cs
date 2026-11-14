using System.Security.Cryptography;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Helpers;
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

    /// <summary>
    /// Streams the body to a temp file next to the target while hashing and checking it. Nothing is visible yet: the
    /// caller calls <see cref="Promote"/> under <see cref="ImagesHelper.LockKey"/> once the quota allows it, and
    /// disposes the result (which deletes the temp file if it was not promoted).
    /// </summary>
    /// <param name="quotaHint">Early stop past this many bytes (a cheap pre-check, not the authoritative quota check).</param>
    public async Task<StagedImage> StageAsync(string sha256, Stream body, long? quotaHint, CancellationToken ct)
    {
        var finalPath = PathFor(sha256);
        var dir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Guid.NewGuid():N}.tmp");
        var staged = new StagedImage(sha256, temp);
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
                    if (total + n > MaxBytes) return staged.Failed(ImageSaveStatus.TooLarge);
                    if (quotaHint is { } free && total + n > free) return staged.Failed(ImageSaveStatus.QuotaExceeded);
                    for (var i = 0; i < n && headLen < head.Length; i++) head[headLen++] = buffer[i];
                    tail1 = n >= 2 ? buffer[n - 2] : tail2;
                    tail2 = buffer[n - 1];
                    total += n;
                    hash.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            if (!LooksLikeJpeg(head, headLen, tail1, tail2, total))
                return staged.Failed(ImageSaveStatus.NotJpeg);
            if (!Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(sha256, StringComparison.Ordinal))
                return staged.Failed(ImageSaveStatus.HashMismatch);
            staged.Status = ImageSaveStatus.Saved;
            staged.Size = total;
            return staged;
        }
        catch
        {
            staged.Dispose();
            throw;
        }
    }

    /// <summary>Moves a staged photo into place (atomically). Call it under <see cref="ImagesHelper.LockKey"/>.</summary>
    public void Promote(StagedImage staged)
    {
        if (staged.Status != ImageSaveStatus.Saved) throw new InvalidOperationException("Only a valid staged photo can be promoted.");
        File.Move(staged.TempPath, PathFor(staged.Sha256), overwrite: true);
        staged.Promoted = true;
        LogStored(staged.Sha256, staged.Size);
    }

    /// <summary>Deletes the photo file, if any.</summary>
    public void DeleteFile(string sha256)
    {
        try { File.Delete(PathFor(sha256)); } catch (IOException) { }
    }

    /// <summary>
    /// Inserts the <c>image</c> row of a staged photo, under the quota. Call it in a transaction holding
    /// <see cref="ImagesHelper.LockKey"/> (<see cref="LockAsync"/>), so the used total cannot change between the check
    /// and the insert. Already stored: <see cref="ImageSaveStatus.Saved"/> with no new bytes.
    /// </summary>
    public static async Task<(ImageSaveStatus Status, bool Inserted)> ReserveAsync(
        EasyStockDbContext db, string sha256, long size, long quotaBytes, TimeProvider time, CancellationToken ct)
    {
        if (await db.Set<ImageRecord>().AnyAsync(i => i.Sha256 == sha256, ct)) return (ImageSaveStatus.Saved, false);
        if (await UsedBytesAsync(db, ct) + size > quotaBytes) return (ImageSaveStatus.QuotaExceeded, false);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO image (sha256, size, created_at) VALUES ({sha256}, {size}, {time.GetUtcNow().ToUnixTimeMilliseconds()})", ct);
        return (ImageSaveStatus.Saved, true);
    }

    /// <summary>Takes the image advisory lock for the current transaction. Lock order: the sync lock first, then this one.</summary>
    public static Task LockAsync(EasyStockDbContext db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The image lock is transaction-scoped: begin a transaction first.");
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ImagesHelper.LockKey})", ct);
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
    /// Deletes the <c>image</c> rows no article references (soft-deleted articles included: a tombstone keeps its
    /// photo), if the photo was stored before <paramref name="olderThan"/> ago (a photo is uploaded before the push
    /// that references it). Call it in a transaction holding the sync lock (no push in flight can reference a photo
    /// unseen) and then <see cref="LockAsync"/> (no upload races the delete). Returns the deleted hashes: the caller
    /// removes the files with <see cref="DeleteFile"/> before the commit, while the image lock is still held (after
    /// it, a waiting upload of the same photo could move its file in and lose it). A failed commit then leaves an
    /// unreferenced row without file, which the next run removes.
    /// </summary>
    public static async Task<List<string>> DeleteOrphanRowsAsync(EasyStockDbContext db, TimeSpan olderThan, TimeProvider time, CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().Add(-olderThan).ToUnixTimeMilliseconds();
        var orphans = await db.Set<ImageRecord>()
            .Where(i => i.CreatedAt < cutoff && !db.Set<Article>().Any(a => a.ImageSha256 == i.Sha256))
            .Select(i => i.Sha256).ToListAsync(ct);
        if (orphans.Count > 0)
            await db.Set<ImageRecord>().Where(i => orphans.Contains(i.Sha256)).ExecuteDeleteAsync(ct);
        return orphans;
    }

    /// <summary>
    /// <see cref="DeleteOrphanRowsAsync"/> and the files, in a transaction of its own. Returns how many were deleted.
    /// </summary>
    public async Task<int> DeleteOrphansAsync(EasyStockDbContext db, TimeSpan olderThan, TimeProvider time, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AcquireSyncLockAsync(ct);
        await LockAsync(db, ct);
        var deleted = await DeleteOrphanRowsAsync(db, olderThan, time, ct);
        foreach (var sha in deleted) DeleteFile(sha);
        await tx.CommitAsync(ct);
        return deleted.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored image {Sha256} ({Size} bytes)")]
    private partial void LogStored(string sha256, long size);
}
