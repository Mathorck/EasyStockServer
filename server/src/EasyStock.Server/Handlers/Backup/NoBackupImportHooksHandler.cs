using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Enums;

namespace EasyStock.Server.Handlers.Backup;

/// <summary>Default until the epoch and push services exist: does nothing.</summary>
public sealed class NoBackupImportHooksHandler : IBackupImportHooksHandler
{
    public Task BeforeCommitAsync(BackupImportMode mode, EasyStockDbContext db, CancellationToken ct) => Task.CompletedTask;

    public Task AfterCommitAsync(BackupImportMode mode, CancellationToken ct) => Task.CompletedTask;
}
