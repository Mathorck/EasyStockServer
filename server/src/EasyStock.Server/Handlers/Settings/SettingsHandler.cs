using System.Text.Json;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Settings;

/// <summary>Reads and writes the typed server settings in the `setting` table. A missing row means the default. Rows with other keys are ignored.</summary>
public sealed class SettingsHandler(EasyStockDbContext db)
{
    private const string ServerNameKey = "server_name";

    public async Task<ServerSettings> GetAsync(CancellationToken ct = default)
    {
        var rows = await db.Set<Setting>().AsNoTracking()
            .Where(s => s.Key == ServerNameKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new ServerSettings(Read<string>(rows, ServerNameKey) ?? ServerSettings.DefaultServerName);
    }

    /// <summary>Replaces the settings. The caller validates first. One upsert statement, so two concurrent first saves cannot hit the unique key.</summary>
    public async Task SaveAsync(ServerSettings settings, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings.ServerName);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO setting (key, value) VALUES ({ServerNameKey}, {json}::jsonb) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value",
            ct);
    }

    private static T? Read<T>(IReadOnlyDictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default;
}
