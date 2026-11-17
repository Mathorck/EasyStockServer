using System.Text.Json;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// <c>GET /me/data</c>: the caller's personal data as JSON (GDPR art. 15 and 20, nFADP art. 25 and 28). Holds the
/// account, the device sessions, the push registrations (endpoint host only, the URL holds a secret), the
/// low-stock alerts the user caused and the movements the user made. Never password hashes, security stamps, tokens or token hashes, and nothing of other
/// users. The movements are streamed from the database to the response, so a long history is never held in memory.
/// </summary>
public sealed class PersonalDataHandler(EasyStockDbContext db, IOptions<ServerOptions> options, TimeProvider time)
{
    public const int FormatVersion = 1;

    /// <summary>Movements written between two flushes of the response.</summary>
    private const int FlushEvery = 500;

    public string FileName() => $"easystock-my-data-{time.GetUtcNow().UtcDateTime:yyyy-MM-dd}.json";

    public async Task WriteAsync(Guid userId, Stream body, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var sessions = await db.DeviceSessions.AsNoTracking().Where(s => s.UserId == userId)
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.DeviceName, s.CreatedAt, s.LastUsedAt, s.RevokedAt, s.RevokeReason })
            .ToListAsync(ct);
        var pushes = await db.Set<PushRegistration>().AsNoTracking()
            .Where(p => db.DeviceSessions.Any(s => s.Id == p.DeviceSessionId && s.UserId == userId))
            .OrderBy(p => p.CreatedAt)
            .Select(p => new { p.DeviceSessionId, p.Endpoint, p.CreatedAt, p.UpdatedAt })
            .ToListAsync(ct);

        await using var w = new Utf8JsonWriter(body, new JsonWriterOptions { Indented = true });
        w.WriteStartObject();
        w.WriteNumber("formatVersion", FormatVersion);
        w.WriteString("exportedAt", now.UtcDateTime);
        w.WriteStartObject("server");
        w.WriteString("publicUrl", options.Value.PublicUrl);
        w.WriteEndObject();

        w.WriteStartObject("account");
        w.WriteString("id", user.Id);
        w.WriteString("username", user.UserName);
        w.WriteString("displayName", user.DisplayName);
        w.WriteString("role", user.Role.ToWire());
        w.WriteString("kind", user.Kind.KindToWire());
        w.WriteString("shortCode", ShortCodeHelper.Format(user.ShortCode));
        w.WriteString("createdAt", user.CreatedAt.UtcDateTime);
        WriteTime(w, "lastSeenAt", user.LastSeenAt);
        if (user.AlertsEnabled is { } alerts) w.WriteBoolean("alertsEnabled", alerts);
        else w.WriteNull("alertsEnabled");
        w.WriteBoolean("disabled", user.DisabledAt is not null);
        w.WriteEndObject();

        w.WriteStartArray("devices");
        foreach (var s in sessions)
        {
            w.WriteStartObject();
            w.WriteString("id", s.Id);
            w.WriteString("name", s.DeviceName);
            w.WriteString("createdAt", s.CreatedAt.UtcDateTime);
            w.WriteString("lastSeenAt", s.LastUsedAt.UtcDateTime);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // One session per device: the session part of the same rows, without the token hashes.
        w.WriteStartArray("sessions");
        foreach (var s in sessions)
        {
            w.WriteStartObject();
            w.WriteString("deviceId", s.Id);
            w.WriteString("createdAt", s.CreatedAt.UtcDateTime);
            w.WriteString("lastUsedAt", s.LastUsedAt.UtcDateTime);
            WriteTime(w, "expiresAt", s.RevokedAt is null ? s.LastUsedAt + AuthLimitsHelper.RefreshIdleLifetime : null);
            WriteTime(w, "revokedAt", s.RevokedAt);
            w.WriteString("revokeReason", s.RevokeReason);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("pushRegistrations");
        foreach (var p in pushes)
        {
            w.WriteStartObject();
            w.WriteString("deviceId", p.DeviceSessionId);
            w.WriteString("endpointHost", Uri.TryCreate(p.Endpoint, UriKind.Absolute, out var uri) ? uri.Host : null);
            w.WriteString("createdAt", FromEpochMs(p.CreatedAt));
            w.WriteString("updatedAt", FromEpochMs(p.UpdatedAt));
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // Low-stock alerts a movement of the user caused (low_stock_alert.caused_by_user_id).
        w.WriteStartArray("lowStockAlerts");
        var lowStockAlerts = (
            from x in db.Set<LowStockAlert>().AsNoTracking()
            where x.CausedByUserId == userId
            join a in db.Articles on x.ArticleId equals a.Id into articles
            from a in articles.DefaultIfEmpty()
            orderby x.CrossedAt, x.Id
            select new { x.Id, x.ArticleId, ArticleName = a.Name, x.CrossedAt, x.Stock }).AsAsyncEnumerable();
        var n = 0;
        await foreach (var x in lowStockAlerts.WithCancellation(ct))
        {
            w.WriteStartObject();
            w.WriteString("id", x.Id);
            w.WriteString("articleId", x.ArticleId);
            w.WriteString("articleName", x.ArticleName);
            w.WriteString("crossedAt", FromEpochMs(x.CrossedAt));
            w.WriteNumber("stock", x.Stock);
            w.WriteEndObject();
            if (++n % FlushEvery == 0) await w.FlushAsync(ct);
        }
        w.WriteEndArray();

        w.WriteStartArray("movements");
        var movements = (
            from m in db.StockMovements.AsNoTracking()
            where m.CreatedBy == userId
            join a in db.Articles on m.ArticleId equals a.Id into articles
            from a in articles.DefaultIfEmpty()
            join l in db.Locations on m.LocationId equals l.Id into locations
            from l in locations.DefaultIfEmpty()
            orderby m.OccurredAt, m.Id
            select new
            {
                m.Id, m.OccurredAt, m.CreatedAt, m.ArticleId, ArticleName = a.Name, m.ItemId, m.LocationId,
                LocationName = l.Name, m.Quantity, m.Reason, m.Comment, m.DeviceId,
            }).AsAsyncEnumerable();
        await foreach (var m in movements.WithCancellation(ct))
        {
            w.WriteStartObject();
            w.WriteString("id", m.Id);
            w.WriteString("occurredAt", FromEpochMs(m.OccurredAt));
            w.WriteString("createdAt", FromEpochMs(m.CreatedAt));
            w.WriteString("articleId", m.ArticleId);
            w.WriteString("articleName", m.ArticleName);
            WriteGuid(w, "itemId", m.ItemId);
            w.WriteString("locationId", m.LocationId);
            w.WriteString("locationName", m.LocationName);
            w.WriteNumber("quantity", m.Quantity);
            w.WriteString("reason", m.Reason);
            w.WriteString("comment", m.Comment);
            WriteGuid(w, "deviceId", m.DeviceId);
            w.WriteEndObject();
            if (++n % FlushEvery == 0) await w.FlushAsync(ct);
        }
        w.WriteEndArray();
        w.WriteEndObject();
        await w.FlushAsync(ct);
    }

    private static DateTime FromEpochMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    private static void WriteTime(Utf8JsonWriter w, string name, DateTimeOffset? value)
    {
        if (value is { } v) w.WriteString(name, v.UtcDateTime);
        else w.WriteNull(name);
    }

    private static void WriteGuid(Utf8JsonWriter w, string name, Guid? value)
    {
        if (value is { } v) w.WriteString(name, v);
        else w.WriteNull(name);
    }
}
