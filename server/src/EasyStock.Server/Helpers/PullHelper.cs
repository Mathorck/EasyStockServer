using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Sync;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Server.Helpers;

public static class PullHelper
{
    /// <summary>GET /api/v1/sync/pull. Users come from the users table. Needs <see cref="DatasetEpochHelper.AddDatasetEpoch"/>.</summary>
    public static IServiceCollection AddSyncPull(this IServiceCollection services)
    {
        services.AddScoped<PullHandler>();
        services.TryAddScoped<IPullUserDirectoryHandler, PullUserDirectoryHandler>();
        return services;
    }
}
