using EasyStock.Server.Handlers.Sync;

namespace EasyStock.Server.Helpers;

public static class DatasetEpochHelper
{
    /// <summary>
    /// Registers the <see cref="DatasetEpochHandler"/> singleton. With <paramref name="loadOnStart"/> it also runs at
    /// start (register it after the migrator); without it (OpenAPI generator) the value loads on first use.
    /// </summary>
    public static IServiceCollection AddDatasetEpoch(this IServiceCollection services, bool loadOnStart)
    {
        services.AddSingleton<DatasetEpochHandler>();
        if (loadOnStart)
            services.AddHostedService(sp => sp.GetRequiredService<DatasetEpochHandler>());
        return services;
    }
}
