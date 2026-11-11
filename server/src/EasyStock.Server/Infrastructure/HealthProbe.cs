namespace EasyStock.Server.Infrastructure;

/// <summary>Used by the Docker HEALTHCHECK: the runtime image has no curl or wget.</summary>
public static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync("http://127.0.0.1:8080/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}
