using EasyStock.Server.Helpers;

namespace EasyStock.Server.Models;

/// <summary>Limits applied while reading a backup archive (decompressed bytes, entries). Defaults: <see cref="BackupFormatHelper"/>.</summary>
public sealed record BackupLimits(
    long ManifestBytes = BackupFormatHelper.MaxManifestBytes,
    long TableBytes = BackupFormatHelper.MaxTableBytes,
    long ImageBytes = BackupFormatHelper.MaxImageBytes,
    long TotalBytes = BackupFormatHelper.MaxTotalBytes,
    int Entries = BackupFormatHelper.MaxEntries)
{
    public static readonly BackupLimits Default = new();
}
