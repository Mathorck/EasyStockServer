using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Backup;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Server.Helpers;

public static class BackupHelper
{
    /// <summary>
    /// Registers the backup export/import. <see cref="IBackupImageHandler"/> and <see cref="IBackupImportHooksHandler"/> use
    /// <c>TryAdd</c>, so a test can register its own first. The hooks rotate the dataset epoch on a replace.
    /// </summary>
    public static IServiceCollection AddBackup(this IServiceCollection services)
    {
        services.TryAddScoped<IBackupImageHandler, BackupImageHandler>();
        services.TryAddScoped<IBackupImportHooksHandler, BackupImportHooksHandler>();
        services.AddScoped<BackupExportHandler>();
        services.AddScoped<BackupImportHandler>();
        return services;
    }
}
