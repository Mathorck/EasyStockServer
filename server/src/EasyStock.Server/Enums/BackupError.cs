using System.Text.Json;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Backup;

namespace EasyStock.Server.Enums;

/// <summary>Why a backup is refused. Shown to the admin, never contains row data.</summary>
public enum BackupError
{
    NotABackup,
    Encrypted,
    NewerVersion,
    Damaged,
    TooLarge,
    Rejected,
    /// <summary>The backup's new photos do not fit in <c>EASYSTOCK_IMAGE_QUOTA_MB</c>.</summary>
    ImageQuotaExceeded,
}
