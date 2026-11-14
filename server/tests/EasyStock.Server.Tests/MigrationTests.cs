using EasyStock.Server.DB;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class MigrationTests(PostgresFixture pg)
{
    [Fact]
    public async Task Startup_leaves_no_pending_migration()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(pg.ConnectionString);
        factory.CreateClient(); // starts the host, which migrates

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
        Assert.True(await db.Database.CanConnectAsync(ct));
    }

    [Fact]
    public void Unreachable_database_fails_startup()
    {
        using var factory = new ApiFactory("Host=127.0.0.1;Port=1;Database=x;Timeout=2");
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        // The migration's connection attempt fails: an Npgsql error somewhere in the chain, not any startup bug.
        var chain = new List<Exception>();
        for (var e = ex; e != null; e = e.InnerException) chain.Add(e);
        Assert.Contains(chain, e => e is Npgsql.NpgsqlException);
    }
}
