using EasyStock.Server.DB;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Sync;
using EasyStock.Server.Interfaces;

namespace EasyStock.Server.Handlers.Backup;

/// <summary>
/// Replace import: a new dataset epoch, written in the import transaction and served once it is committed. Merge
/// keeps the epoch. No "sync now" push is sent: the server has no push sender yet.
/// </summary>
public sealed class BackupImportHooksHandler(DatasetEpochHandler epochs) : IBackupImportHooksHandler
{
    private string? _newEpoch;

    public async Task BeforeCommitAsync(BackupImportMode mode, EasyStockDbContext db, CancellationToken ct)
    {
        if (mode == BackupImportMode.Replace)
            _newEpoch = await epochs.RotateAsync(db, ct);
    }

    public Task AfterCommitAsync(BackupImportMode mode, CancellationToken ct)
    {
        // First, and synchronous: the import logs and swallows exceptions from this hook.
        if (_newEpoch is { } epoch)
            epochs.Publish(epoch);
        // TODO: push "sync" to every registered session once a push sender exists.
        return Task.CompletedTask;
    }
}
