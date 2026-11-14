using System.Reflection;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using Microsoft.Extensions.Options;

namespace EasyStock.Server;

// Not static: WebApplicationFactory<Program> in the tests needs a type it can use as a generic argument.
public sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--healthcheck"))
            return await HealthProbe.RunAsync();

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddEasyStockLogging();

        // Options are bound and validated when the host starts, so a missing variable fails fast.
        var serverOptions = builder.Services.AddOptions<ServerOptions>()
            .Configure<IConfiguration>((o, c) =>
            {
                var parsed = ServerOptions.From(c);
                o.Db = parsed.Db;
                o.PublicUrl = parsed.PublicUrl;
                o.DataDir = parsed.DataDir;
                o.CertFingerprint = parsed.CertFingerprint;
                o.LogLevel = parsed.LogLevel;
                o.TrustedProxies = parsed.TrustedProxies;
                o.ImageQuotaBytes = parsed.ImageQuotaBytes;
                o.CleanupIntervalMinutes = parsed.CleanupIntervalMinutes;
            });
        // The build-time OpenAPI generator starts the app without any EASYSTOCK_* variables.
        if (!IsOpenApiGenerator)
            serverOptions.ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddEasyStockApi();
        builder.Services.AddEasyStockCompression();
        builder.Services.AddEasyStockOpenApi();
        builder.Services.AddDbContext<EasyStockDbContext>((sp, o) =>
            EasyStockDbContext.Configure(o, sp.GetRequiredService<IOptions<ServerOptions>>().Value.Db));
        builder.Services.AddHealthChecks().AddDbContextCheck<EasyStockDbContext>();
        // The OpenAPI generator has no database to migrate.
        if (!IsOpenApiGenerator)
            builder.Services.AddHostedService<DatabaseMigrator>();
        builder.Services.AddEasyStockAuth(startupSteps: !IsOpenApiGenerator);
        builder.Services.AddEasyStockRateLimiting();
        builder.Services.AddEasyStockForwardedHeaders();
        builder.Services.AddImages();
        builder.Services.AddSettings();
        builder.Services.AddServerInfo();
        builder.Services.AddBackup();
        // The OpenAPI generator has no database to clean.
        builder.Services.AddCleanup(job: !IsOpenApiGenerator);
        builder.Services.AddSyncPull();
        builder.Services.AddSyncPush();
        // After the migrator: creates the dataset epoch on the first start and caches it.
        builder.Services.AddDatasetEpoch(loadOnStart: !IsOpenApiGenerator);

        var app = builder.Build();

        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseEasyStockCompression();
        app.UseMiddleware<VersionHeaderMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        // The API description is only served in Development or when EASYSTOCK_OPENAPI=true: it maps every route
        // for anyone who asks. The build-time generator reads the document from the services, not this route.
        if (IsOpenApiGenerator || ServerOptions.OpenApiEnabled(app.Configuration, app.Environment))
            app.MapOpenApi("/openapi/{documentName}.json");
        app.MapHealthChecks("/health");
        app.MapControllers();

        await app.RunAsync();
        return 0;
    }

    private static bool IsOpenApiGenerator =>
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
