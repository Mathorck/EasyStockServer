using EasyStock.Server.Handlers.ServerInfo;

namespace EasyStock.Server.Helpers;

public static class ServerInfoHelper
{
    /// <summary>Registers <see cref="ServerInfoHandler"/>, which answers <c>GET /server-info</c>.</summary>
    public static IServiceCollection AddServerInfo(this IServiceCollection services)
    {
        services.AddScoped<ServerInfoHandler>();
        return services;
    }
}
