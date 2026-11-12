using System.Text.Json;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Settings;

/// <summary>Reads and writes the typed server settings in the `setting` table. A missing row means the default.</summary>
public sealed class SettingsHandler(EasyStockDbContext db)
{
    private const string ServerNameKey = "server_name";
    private const string RetentionKey = "movement_author_retention_days";

    public async Task<ServerSettings> GetAsync(CancellationToken ct = default)
    {
        var rows = await db.Set<Setting>().AsNoTracking()
            .Where(s => s.Key == ServerNameKey || s.Key == RetentionKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new ServerSettings(
            Read<string>(rows, ServerNameKey) ?? ServerSettings.DefaultServerName,
            Read<int?>(rows, RetentionKey));
    }

    /// <summary>Replaces both values. The caller validates first.</summary>
    public async Task SaveAsync(ServerSettings settings, CancellationToken ct = default)
    {
        var existing = await db.Set<Setting>()
            .Where(s => s.Key == ServerNameKey || s.Key == RetentionKey)
            .ToListAsync(ct);

        Upsert(existing, ServerNameKey, JsonSerializer.Serialize(settings.ServerName));
        Upsert(existing, RetentionKey, JsonSerializer.Serialize(settings.MovementAuthorRetentionDays));
        await db.SaveChangesAsync(ct);
    }

    private void Upsert(List<Setting> existing, string key, string json)
    {
        var row = existing.FirstOrDefault(s => s.Key == key);
        if (row is null)
            db.Set<Setting>().Add(new Setting { Key = key, Value = json });
        else
            row.Value = json;
    }

    private static T? Read<T>(IReadOnlyDictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default;
}
