using Serilog;
using Serilog.Events;

namespace EasyStock.Server.Configuration;

/// <summary>
/// Serilog setup. Levels come from the "Serilog" section (appsettings.json, appsettings.{Environment}.json,
/// or Serilog__MinimumLevel__* variables); EASYSTOCK_LOG_LEVEL, when set, overrides the default level.
/// </summary>
public static class Logging
{
    private const string SqlCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    // Serilog names, plus the Microsoft.Extensions.Logging names people are used to.
    private static readonly Dictionary<string, LogEventLevel> Levels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Verbose"] = LogEventLevel.Verbose,
        ["Trace"] = LogEventLevel.Verbose,
        ["Debug"] = LogEventLevel.Debug,
        ["Information"] = LogEventLevel.Information,
        ["Warning"] = LogEventLevel.Warning,
        ["Error"] = LogEventLevel.Error,
        ["Fatal"] = LogEventLevel.Fatal,
        ["Critical"] = LogEventLevel.Fatal,
    };

    public static IEnumerable<string> LevelNames => Levels.Keys;

    public static bool TryParseLevel(string? value, out LogEventLevel level) =>
        Levels.TryGetValue(value?.Trim() ?? "", out level);

    public static IServiceCollection AddEasyStockLogging(this IServiceCollection services) =>
        // Configured from the built service provider, so settings added by the host or by tests are all visible.
        services.AddSerilog((sp, log) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();

            // Layered on top rather than set with MinimumLevel.Is: the configuration reader installs a level
            // switch for "Default", and a switch wins over a fixed level. An invalid value is reported by
            // ServerOptionsValidator, which fails startup.
            var layered = new ConfigurationBuilder().AddConfiguration(config);
            if (TryParseLevel(config["EASYSTOCK_LOG_LEVEL"], out var level))
                layered.AddInMemoryCollection([new("Serilog:MinimumLevel:Default", level.ToString())]);

            log.ReadFrom.Configuration(layered.Build())
                .ReadFrom.Services(sp)
                .Enrich.FromLogContext()
                .WriteTo.Console();

            // Successful SQL commands (text and timing, logged at Information and below) never reach the log,
            // whatever the configuration says. EF Core still reports failing commands at Error, and those
            // entries can include the command text; parameter values are not logged (no sensitive data logging).
            log.MinimumLevel.Override(SqlCommandCategory, LogEventLevel.Warning);
        },
        // Each host keeps its own logger instead of the static Log.Logger, so hosts running side by side (tests)
        // never write into each other's sinks.
        preserveStaticLogger: true);
}
