using EasyStock.Server.Handlers.Sync;

namespace EasyStock.Server.Helpers;

public static class PushHelper
{
    /// <summary>POST /api/v1/sync/push: the batch processor and the low-stock detector it runs.</summary>
    public static IServiceCollection AddSyncPush(this IServiceCollection services)
    {
        services.AddScoped<PushHandler>();
        services.AddScoped<LowStockHandler>();
        return services;
    }
}
