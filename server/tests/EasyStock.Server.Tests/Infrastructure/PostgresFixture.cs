using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(EasyStock.Server.Tests.Infrastructure.PostgresFixture))]

namespace EasyStock.Server.Tests.Infrastructure;

/// <summary>One PostgreSQL container shared by the whole test run. Needs Docker.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Every database from CreateDatabaseAsync gets its own Npgsql pool, whose idle connections stay open:
    // the default limit of 100 runs out when the test classes run in parallel.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithCommand("-c", "max_connections=1000")
        .Build();

    // CREATE DATABASE copies template1 and fails while another CREATE DATABASE is using it.
    private readonly SemaphoreSlim _create = new(1, 1);

    /// <summary>The shared database: tests on it use unique names and never need an empty server.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>A new empty database on the same container, for tests that need a server nobody has set up.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await _create.WaitAsync();
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _create.Release();
        }
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }
}
