using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace EasyStock.Server.Tests.Infrastructure;

public sealed class ApiFactory(
    string? db,
    string? publicUrl = "https://stock.example.org",
    IReadOnlyDictionary<string, string>? settings = null,
    string? dataDir = null) : WebApplicationFactory<Program>
{
    /// <summary>Test header holding the TCP peer address of a request (the test server has none).</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    // A data directory given by the test is kept (shared by several hosts); an own one is deleted with the host.
    private readonly string _dataDir = dataDir ?? Directory.CreateTempSubdirectory("easystock-tests-").FullName;

    /// <summary>The server clock. It only moves when a test advances it.</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public LogCapture Logs { get; } = new();

    public string DataDir => _dataDir;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Empty values override any real environment variable and count as missing.
        builder.UseSetting("EASYSTOCK_DB", db ?? "");
        builder.UseSetting("EASYSTOCK_PUBLIC_URL", publicUrl ?? "");
        builder.UseSetting("EASYSTOCK_DATA_DIR", _dataDir);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<ILogEventSink>(Logs);
            services.AddTransient<IStartupFilter, RemoteIpStartupFilter>();
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (dataDir == null)
            try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A client that sends acceptable version headers, from its own random client address (so per-IP limits of
    /// one test never hit another), and with a bearer token when given one.
    /// </summary>
    public HttpClient CreateVersionedClient(string? accessToken = null, string? remoteIp = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-EasyStock-App-Version", "1.0.0");
        client.DefaultRequestHeaders.Add("X-EasyStock-Schema-Version", "1");
        client.DefaultRequestHeaders.Add(RemoteIpHeader, remoteIp ?? RandomIp());
        if (accessToken != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static string RandomIp() =>
        $"10.{Random.Shared.Next(0, 200)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    /// <summary>Runs first in the pipeline: sets the peer address before UseForwardedHeaders reads it.</summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext ctx, RequestDelegate nextMiddleware) =>
            {
                if (IPAddress.TryParse(ctx.Request.Headers[RemoteIpHeader].ToString(), out var ip))
                    ctx.Connection.RemoteIpAddress = ip;
                return nextMiddleware(ctx);
            });
            next(app);
        };
    }
}
