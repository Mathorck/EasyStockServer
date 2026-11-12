using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Server.Helpers;

public static class BackupHelper
{
    /// <summary>
    /// Registers the backup export/import. <see cref="IBackupImageHandler"/> and <see cref="IBackupImportHooksHandler"/> use
    /// <c>TryAdd</c>: register the real photo store / sync hooks before this call (or replace them after) to wire them.
    /// </summary>
    public static IServiceCollection AddBackup(this IServiceCollection services)
    {
        services.TryAddScoped<IBackupImageHandler, BackupImageHandler>();
        services.TryAddScoped<IBackupImportHooksHandler, NoBackupImportHooksHandler>();
        services.AddScoped<BackupExportHandler>();
        services.AddScoped<BackupImportHandler>();
        return services;
    }
}
