using System.Text.RegularExpressions;
using EasyStock.Server.Configuration;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Interfaces;

/// <summary>
/// Reads and writes article photos by SHA-256 (64 lower-case hex characters) for backup export and import.
/// <see cref="BackupImageHandler"/> backs it with the image store (files and <c>image</c> rows).
/// </summary>
public interface IBackupImageHandler
{
    /// <summary>Opens the photo, or returns null when the server does not hold it.</summary>
    Task<Stream?> OpenReadAsync(string sha256, CancellationToken ct);

    /// <summary>True when the server holds the photo.</summary>
    Task<bool> ExistsAsync(string sha256, CancellationToken ct);

    /// <summary>
    /// Streams a photo to a temp file and checks it (JPEG, size limit, hash). Nothing is stored yet; the caller
    /// disposes the result. The caller computed the hash. A status other than Saved means it cannot be stored.
    /// </summary>
    Task<StagedImage> StageAsync(string sha256, Stream content, CancellationToken ct);

    /// <summary>
    /// Stores staged photos inside the caller's open transaction (which holds the sync lock): under the image lock,
    /// inserts their <c>image</c> rows within the storage quota and moves the files into place. Throws a
    /// <see cref="BackupException"/> with <see cref="Enums.BackupError.ImageQuotaExceeded"/> past the quota. Returns
    /// the photos moved in and how many rows were new; on a rollback the caller removes the moved files with
    /// <see cref="DeleteFiles"/>.
    /// </summary>
    Task<StoredPhotos> StoreAsync(IReadOnlyCollection<StagedImage> photos, CancellationToken ct);

    /// <summary>Removes photo files (after a rolled-back import).</summary>
    void DeleteFiles(IEnumerable<string> sha256s);
}

/// <param name="Moved">Hashes whose file this call moved into place.</param>
/// <param name="Inserted">New <c>image</c> rows.</param>
public sealed record StoredPhotos(IReadOnlyList<string> Moved, int Inserted);
