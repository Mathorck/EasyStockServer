using EasyStock.Server.Data;
using EasyStock.Server.Endpoints;
using EasyStock.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

if (args.Contains("--healthcheck"))
    return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);

if (Enum.TryParse<LogLevel>(builder.Configuration["EASYSTOCK_LOG_LEVEL"], true, out var level))
    builder.Logging.SetMinimumLevel(level);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

// Options are bound and validated when the host starts, so a missing variable fails fast.
builder.Services.AddOptions<ServerOptions>()
    .Configure<IConfiguration>((o, c) =>
    {
        var parsed = ServerOptions.From(c);
        o.Db = parsed.Db;
        o.PublicUrl = parsed.PublicUrl;
        o.DataDir = parsed.DataDir;
        o.CertFingerprint = parsed.CertFingerprint;
        o.LogLevel = parsed.LogLevel;
        o.TrustedProxies = parsed.TrustedProxies;
    })
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");
builder.Services.AddDbContext<EasyStockDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IOptions<ServerOptions>>().Value.Db).UseSnakeCaseNamingConvention());
builder.Services.AddHealthChecks().AddDbContextCheck<EasyStockDbContext>();

var app = builder.Build();

app.UseMiddleware<VersionHeaderMiddleware>();
app.MapOpenApi("/openapi/{documentName}.json");
app.MapHealthChecks("/health");
app.MapGroup("/api/v1").MapServerInfo();

app.Run();
return 0;

public partial class Program;
