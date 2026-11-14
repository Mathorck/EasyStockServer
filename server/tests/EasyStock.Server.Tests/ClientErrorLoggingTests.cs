using EasyStock.Server.Tests.Infrastructure;
using Serilog.Events;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class ClientErrorLoggingTests(PostgresFixture pg)
{
    [Fact]
    public async Task Client_errors_are_not_logged_at_error_level()
    {
        using var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        var response = await factory.LoginAsync("nobody", "wrong password");
        Assert.True((int)response.StatusCode is >= 400 and < 500);

        // Startup migrations on an empty database log a failed EF history query at Error; only request handling matters here.
        Assert.DoesNotContain(factory.Logs.Events, e => e.Level >= LogEventLevel.Error
            && e.Properties.TryGetValue("SourceContext", out var c) && c.ToString().Contains("Microsoft.AspNetCore"));
    }
}
