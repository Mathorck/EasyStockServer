using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(EasyStock.Server.Tests.Infrastructure.PostgresFixture))]

namespace EasyStock.Server.Tests.Infrastructure;

/// <summary>One PostgreSQL container shared by the whole test run. Needs Docker.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
