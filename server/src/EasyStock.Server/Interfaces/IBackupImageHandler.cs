using System.Text.RegularExpressions;
using EasyStock.Server.Configuration;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
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
    /// Stores a photo under its hash (content-addressed: nothing happens when it is already there). The caller computed
    /// the hash. False when the photo was not stored (not a JPEG, too large, or not matching its hash).
    /// </summary>
    Task<bool> WriteAsync(string sha256, Stream content, CancellationToken ct);
}
