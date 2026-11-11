using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EasyStock.Server.Tests.Infrastructure;

public sealed class ApiFactory(string? db, string? publicUrl = "https://stock.example.org") : WebApplicationFactory<Program>
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("easystock-tests-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Empty values override any real environment variable and count as missing.
        builder.UseSetting("EASYSTOCK_DB", db ?? "");
        builder.UseSetting("EASYSTOCK_PUBLIC_URL", publicUrl ?? "");
        builder.UseSetting("EASYSTOCK_DATA_DIR", _dataDir);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A client that sends acceptable version headers.</summary>
    public HttpClient CreateVersionedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-EasyStock-App-Version", "1.0.0");
        client.DefaultRequestHeaders.Add("X-EasyStock-Schema-Version", "1");
        return client;
    }
}
