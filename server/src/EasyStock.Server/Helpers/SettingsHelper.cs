using EasyStock.Server.Handlers.Settings;

namespace EasyStock.Server.Helpers;

public static class SettingsHelper
{
    /// <summary>Registers <see cref="SettingsHandler"/>, which reads the Admin settings from the database.</summary>
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        services.AddScoped<SettingsHandler>();
        return services;
    }
}
