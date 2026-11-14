using System.Text.Json;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Sync;

/// <summary>
/// The dataset epoch (docs/03-server-spec.md, "Dataset epoch"): a random UUID in the <c>setting</c> table, changed by
/// every replace import. Phones send it back in <see cref="HeaderName"/>; a different value means their data is stale.
/// Singleton: the value is read from the database once (at start, or on first use) and then served from memory, so
/// the check costs nothing per request. Only the import changes it, and only after its commit (<see cref="Publish"/>).
/// One server instance is assumed: a second instance would keep its old value until restarted.
/// </summary>
public sealed class DatasetEpochHandler(IServiceScopeFactory scopes) : IHostedService
{
    public const string HeaderName = "X-EasyStock-Dataset-Epoch";
    private const string SettingKey = "dataset_epoch";

    private string? _current;

    /// <summary>The current epoch, from memory after the first call.</summary>
    public ValueTask<string> GetAsync(CancellationToken ct) =>
        Volatile.Read(ref _current) is { } epoch ? ValueTask.FromResult(epoch) : new ValueTask<string>(LoadAsync(ct));

    /// <summary>A missing, empty or whitespace-only header counts as absent and is accepted (first sync, older app); anything else must equal the epoch.</summary>
    public static bool Accepts(string? header, string current) =>
        string.IsNullOrWhiteSpace(header) || string.Equals(header.Trim(), current, StringComparison.OrdinalIgnoreCase);

    /// <summary>The stored epoch, read in the caller's transaction (null if the row is missing).</summary>
    public async Task<string?> ReadAsync(EasyStockDbContext db, CancellationToken ct)
    {
        var json = await db.Set<Setting>().AsNoTracking()
            .Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<string>(json);
    }

    /// <summary>Writes a new epoch in the caller's transaction and returns it. Call <see cref="Publish"/> after the commit.</summary>
    public async Task<string> RotateAsync(EasyStockDbContext db, CancellationToken ct)
    {
        var epoch = Guid.NewGuid().ToString("D");
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO setting (key, value) VALUES ({SettingKey}, to_jsonb({epoch}::text)) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value",
            ct);
        return epoch;
    }

    /// <summary>Makes a committed epoch the current one.</summary>
    public void Publish(string epoch) => Volatile.Write(ref _current, epoch);

    /// <summary>Creates the epoch if missing (race-safe across hosts), then caches the stored one.</summary>
    private async Task<string> LoadAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var fresh = Guid.NewGuid().ToString("D");
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO setting (key, value) VALUES ({SettingKey}, to_jsonb({fresh}::text)) ON CONFLICT (key) DO NOTHING", ct);
        var stored = await ReadAsync(db, ct) ?? fresh;
        // An import that published while this load ran wins: its value is newer than the one read here.
        return Interlocked.CompareExchange(ref _current, stored, null) ?? stored;
    }

    /// <summary>Runs after the migrations: creates the epoch on the first start and fills the cache.</summary>
    public Task StartAsync(CancellationToken ct) => GetAsync(ct).AsTask();

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
