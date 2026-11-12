using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Interfaces;

/// <summary>
/// What an import must tell the rest of the server. The orchestrator wires the real ones: rotate the dataset epoch
/// on a replace (docs/04, item 21) and send the "sync now" push after any import.
/// </summary>
public interface IBackupImportHooksHandler
{
    /// <summary>
    /// Inside the import transaction, after the data is written and before the commit (the sync lock is held).
    /// Replace: generate a new dataset epoch here, so it commits with the new data.
    /// </summary>
    Task BeforeCommitAsync(BackupImportMode mode, EasyStockDbContext db, CancellationToken ct);

    /// <summary>After the commit: push "sync" to every registered session. A failure must not fail the import.</summary>
    Task AfterCommitAsync(BackupImportMode mode, CancellationToken ct);
}
