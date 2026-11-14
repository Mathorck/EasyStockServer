using EasyStock.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EasyStock.Server.Tests;

// Hosts started in parallel by other test classes can pick up these settings, or hide them.
[CollectionDefinition(nameof(LoggingTests), DisableParallelization = true)]
public sealed class LoggingCollection;

[Collection(nameof(LoggingTests))]
public sealed class LoggingTests(PostgresFixture pg)
{
    // Startup applies migrations, so it needs a reachable database.
    private string Db => pg.ConnectionString;

    [Fact]
    public void Default_level_comes_from_appsettings()
    {
        using var factory = new ApiFactory(Db);
        var logs = factory.Services.GetRequiredService<ILoggerFactory>();
        Assert.True(logs.CreateLogger("EasyStock").IsEnabled(LogLevel.Information));
        Assert.False(logs.CreateLogger("EasyStock").IsEnabled(LogLevel.Debug));
        Assert.False(logs.CreateLogger("Microsoft.AspNetCore.Routing").IsEnabled(LogLevel.Information));
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("trace")]
    public void Env_level_overrides_appsettings(string value)
    {
        using var factory = WithSetting("EASYSTOCK_LOG_LEVEL", value);
        var logs = factory.Services.GetRequiredService<ILoggerFactory>();
        Assert.True(logs.CreateLogger("EasyStock").IsEnabled(LogLevel.Debug));
    }

    [Fact]
    public void Serilog_section_can_be_set_from_environment_style_keys()
    {
        using var factory = WithSetting("Serilog:MinimumLevel:Default", "Warning");
        var logs = factory.Services.GetRequiredService<ILoggerFactory>();
        Assert.False(logs.CreateLogger("EasyStock").IsEnabled(LogLevel.Information));
        Assert.True(logs.CreateLogger("EasyStock").IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public void Sql_commands_stay_at_warning_even_in_verbose_mode()
    {
        using var factory = WithSetting("EASYSTOCK_LOG_LEVEL", "Verbose");
        var logs = factory.Services.GetRequiredService<ILoggerFactory>();
        Assert.False(logs.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void Invalid_env_level_fails_startup()
    {
        using var factory = WithSetting("EASYSTOCK_LOG_LEVEL", "loud");
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains("EASYSTOCK_LOG_LEVEL", ex.ToString());
    }

    // The settings go to the one factory, which the test disposes (no throwaway parent factory is left behind).
    private ApiFactory WithSetting(string key, string value) =>
        new(Db, settings: new Dictionary<string, string> { [key] = value });
}
