using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.DB;

/// <summary>
/// Applies pending EF Core migrations when the service starts. It runs in StartingAsync, which comes after
/// options validation and before any hosted service (including Kestrel) starts, so no request ever sees an
/// old schema. A failure stops the startup. A session advisory lock makes instances starting together migrate
/// one after the other: EF Core's own lock does not stop a second instance from applying the same migration.
/// </summary>
public sealed partial class DatabaseMigrator(IServiceScopeFactory scopes, ILogger<DatabaseMigrator> log) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        // EF logs each migration it applies (category Microsoft.EntityFrameworkCore.Migrations).
        LogMigrating(log);
        await db.Database.OpenConnectionAsync(ct); // the lock belongs to this connection, MigrateAsync reuses it
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({MigrationLockKey})", ct);
            try
            {
                await db.Database.MigrateAsync(ct);
            }
            finally
            {
                // Explicit: closing only returns the connection to the pool, the session (and its lock) stays.
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({MigrationLockKey})", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
        LogMigrated(log);
    }

    private const long MigrationLockKey = 0x4553_4D49_4752; // "ESMIGR", distinct from ServerSeq.SyncLockKey

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying database migrations")]
    private static partial void LogMigrating(ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date")]
    private static partial void LogMigrated(ILogger log);
}
