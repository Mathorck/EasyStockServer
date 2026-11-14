namespace EasyStock.Server.Configuration;

/// <summary>Used by the Docker HEALTHCHECK: the runtime image has no curl or wget.</summary>
public static class HealthProbe
{
    private const int DefaultPort = 8080;

    /// <summary>
    /// The first plain-HTTP port the server listens on: ASPNETCORE_HTTP_PORTS (first entry), else the first
    /// http:// entry of ASPNETCORE_URLS (or URLS), else 8080.
    /// </summary>
    public static int ResolvePort(Func<string, string?> env)
    {
        var ports = env("ASPNETCORE_HTTP_PORTS");
        if (!string.IsNullOrWhiteSpace(ports))
        {
            foreach (var entry in ports.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(entry, out var port) && port is > 0 and <= 65535) return port;
        }
        var urls = env("ASPNETCORE_URLS") ?? env("DOTNET_URLS");
        if (!string.IsNullOrWhiteSpace(urls))
        {
            foreach (var entry in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // Wildcard hosts (+, *) are not valid URI hosts; swap in a placeholder to read the port.
                var candidate = entry.Replace("://+", "://localhost").Replace("://*", "://localhost");
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttp && u.Port > 0)
                    return u.Port;
            }
        }
        return DefaultPort;
    }

    public static async Task<int> RunAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync($"http://127.0.0.1:{ResolvePort(Environment.GetEnvironmentVariable)}/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}
