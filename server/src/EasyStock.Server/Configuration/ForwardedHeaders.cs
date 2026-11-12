using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace EasyStock.Server.Configuration;

/// <summary>
/// X-Forwarded-For and X-Forwarded-Proto are only believed from loopback and the EASYSTOCK_TRUSTED_PROXIES
/// entries (docs/04-server-implementation-plan.md, 3.9), so rate limits use the real client address.
/// </summary>
public static class ForwardedHeaders
{
    public static IServiceCollection AddEasyStockForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((o, config) =>
        {
            o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("127.0.0.0/8"));
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("::1/128"));
            // Read from the configuration rather than IOptions<ServerOptions>: the middleware is built with the app,
            // also for the OpenAPI generator, which has no settings. Entries are checked at startup
            // (ServerOptionsValidator); a bad one is skipped here.
            foreach (var entry in ServerOptions.From(config).TrustedProxies)
            {
                if (entry.Contains('/'))
                {
                    if (System.Net.IPNetwork.TryParse(entry, out var network)) o.KnownIPNetworks.Add(network);
                }
                else if (IPAddress.TryParse(entry, out var address))
                {
                    o.KnownProxies.Add(address);
                }
            }
        });
        return services;
    }
}
