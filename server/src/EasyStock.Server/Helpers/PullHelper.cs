using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Sync;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Server.Helpers;

public static class PullHelper
{
    /// <summary>
    /// GET /api/v1/sync/pull. Users come from the users table. <see cref="IPullDatasetEpochHandler"/> uses TryAdd:
    /// a real one registered before this call wins over the null placeholder (no dataset epoch exists yet).
    /// </summary>
    public static IServiceCollection AddSyncPull(this IServiceCollection services)
    {
        services.AddScoped<PullHandler>();
        services.TryAddScoped<IPullUserDirectoryHandler, PullUserDirectoryHandler>();
        services.TryAddScoped<IPullDatasetEpochHandler, NullPullDatasetEpochHandler>();
        return services;
    }
}
