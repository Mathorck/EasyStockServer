using System.Net;
using System.Text.Json;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary><c>GET /me/data</c>: the personal data export (GDPR art. 15 and 20).</summary>
public sealed class MeDataTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, publicUrl: "https://stock.example.org");

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string PushSecret = "upPUSHSECRETabc123";

    private Task<HttpResponseMessage> GetData(string accessToken) =>
        _factory.CreateVersionedClient(accessToken).GetAsync("/api/v1/me/data", Ct);

    private async Task<(Guid Mine, Guid Theirs)> AddMovements(Guid me, Guid other)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "cat" + Guid.NewGuid().ToString("N")[..8], CreatedAt = 1, UpdatedAt = 1 };
        var location = new Location { Id = Guid.NewGuid(), Name = "Shelf A", CreatedAt = 1, UpdatedAt = 1 };
        var article = new Article { Id = Guid.NewGuid(), Name = "Screws", CategoryId = category.Id, Threshold = 0, Attributes = "{}", CreatedAt = 1, UpdatedAt = 1 };
        StockMovement Movement(Guid by, long qty) => new()
        {
            Id = Guid.NewGuid(), ArticleId = article.Id, LocationId = location.Id, Quantity = qty, Reason = "consumption",
            OccurredAt = 1762859000000, CreatedAt = 1762859000000, CreatedBy = by, DeviceId = Guid.NewGuid(),
        };
        var mine = Movement(me, -2500);
        var theirs = Movement(other, 4000);
        await _factory.WithDb(async db =>
        {
            foreach (object row in new object[] { category, location, article, mine, theirs })
            {
                db.Add(row);
                await db.SaveChangesAsync(Ct);
            }
        });
        return (mine.Id, theirs.Id);
    }

    [Fact]
    public async Task Export_holds_the_callers_data_and_no_secrets()
    {
        var user = await _factory.CreateUserAsync(Role.Member);
        var other = await _factory.CreateUserAsync(Role.Member);
        var (mine, theirs) = await AddMovements(user.Id, other.Id);
        Guid sessionId = default;
        await _factory.WithDb(async db =>
        {
            sessionId = await db.DeviceSessions.Where(s => s.UserId == user.Id).Select(s => s.Id).SingleAsync(Ct);
            db.Set<PushRegistration>().Add(new PushRegistration
            {
                DeviceSessionId = sessionId, Endpoint = $"https://ntfy.example.org/{PushSecret}?up=1", CreatedAt = 1762859000000, UpdatedAt = 1762859000000,
            });
            await db.SaveChangesAsync(Ct);
        });

        var response = await GetData(user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Equal($"easystock-my-data-{_factory.Time.GetUtcNow().UtcDateTime:yyyy-MM-dd}.json", disposition?.FileName?.Trim('"'));

        var body = await response.Content.ReadAsStringAsync(Ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.EndsWith("Z", root.GetProperty("exportedAt").GetString());
        Assert.Equal("https://stock.example.org", root.GetProperty("server").GetProperty("publicUrl").GetString());

        var account = root.GetProperty("account");
        Assert.Equal(user.Id, account.GetProperty("id").GetGuid());
        Assert.Equal(user.Username, account.GetProperty("username").GetString());
        Assert.Equal("Test user", account.GetProperty("displayName").GetString());
        Assert.Equal("member", account.GetProperty("role").GetString());
        Assert.Equal("full", account.GetProperty("kind").GetString());
        Assert.Equal(user.Tokens.User.ShortCode, account.GetProperty("shortCode").GetString());
        foreach (var name in new[] { "createdAt", "lastSeenAt", "alertsEnabled", "disabled" })
            Assert.True(account.TryGetProperty(name, out _), name);
        Assert.False(account.GetProperty("disabled").GetBoolean());

        var device = root.GetProperty("devices").EnumerateArray().Single();
        Assert.Equal(sessionId, device.GetProperty("id").GetGuid());
        Assert.Equal("Pixel", device.GetProperty("name").GetString());
        foreach (var name in new[] { "createdAt", "lastSeenAt" }) Assert.True(device.TryGetProperty(name, out _), name);

        var session = root.GetProperty("sessions").EnumerateArray().Single();
        Assert.Equal(sessionId, session.GetProperty("deviceId").GetGuid());
        foreach (var name in new[] { "createdAt", "lastUsedAt", "expiresAt", "revokedAt", "revokeReason" })
            Assert.True(session.TryGetProperty(name, out _), name);

        var push = root.GetProperty("pushRegistrations").EnumerateArray().Single();
        Assert.Equal(sessionId, push.GetProperty("deviceId").GetGuid());
        Assert.Equal("ntfy.example.org", push.GetProperty("endpointHost").GetString());
        Assert.Equal("2025-11-11T11:03:20Z", push.GetProperty("createdAt").GetString());

        var movement = root.GetProperty("movements").EnumerateArray().Single();
        Assert.Equal(mine, movement.GetProperty("id").GetGuid());
        Assert.Equal(-2500, movement.GetProperty("quantity").GetInt64());
        Assert.Equal("Screws", movement.GetProperty("articleName").GetString());
        Assert.Equal("Shelf A", movement.GetProperty("locationName").GetString());
        Assert.Equal("consumption", movement.GetProperty("reason").GetString());
        foreach (var name in new[] { "occurredAt", "createdAt", "articleId", "itemId", "locationId", "comment", "deviceId" })
            Assert.True(movement.TryGetProperty(name, out _), name);

        // Nothing of the other user, no secret.
        Assert.DoesNotContain(theirs.ToString(), body);
        Assert.DoesNotContain(other.Id.ToString(), body);
        Assert.DoesNotContain(other.Username, body);
        Assert.DoesNotContain(PushSecret, body);
        Assert.DoesNotContain(user.RefreshToken, body);
        Assert.DoesNotContain(user.AccessToken, body);
        foreach (var key in new[] { "password", "passwordHash", "securityStamp", "concurrencyStamp", "token", "tokenHash",
                     "refreshToken", "refreshTokenHash", "previousTokenHash", "endpoint" })
            Assert.DoesNotContain($"\"{key}\"", body, StringComparison.OrdinalIgnoreCase);
        await _factory.WithDb(async db =>
        {
            var hash = await db.Users.Where(u => u.Id == user.Id).Select(u => u.PasswordHash).SingleAsync(Ct);
            Assert.DoesNotContain(hash!, body);
        });
    }

    [Fact]
    public async Task Guests_can_export_too()
    {
        var guest = await _factory.SignInAsync(Role.Guest);
        var response = await GetData(guest.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var account = doc.RootElement.GetProperty("account");
        Assert.Equal(guest.UserId, account.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, account.GetProperty("username").ValueKind);
        Assert.Equal("guest", account.GetProperty("role").GetString());
        Assert.Equal("guest", account.GetProperty("kind").GetString());
        Assert.Single(doc.RootElement.GetProperty("devices").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("movements").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("pushRegistrations").EnumerateArray());
    }

    [Fact]
    public async Task Export_is_limited_to_5_per_hour_per_user()
    {
        var user = await _factory.CreateUserAsync(Role.Viewer);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await GetData(user.AccessToken)).StatusCode);
        var limited = await GetData(user.AccessToken);
        await limited.Problem(HttpStatusCode.TooManyRequests, "rate-limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // Another user is not affected.
        var other = await _factory.CreateUserAsync(Role.Viewer);
        Assert.Equal(HttpStatusCode.OK, (await GetData(other.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Export_needs_a_valid_token() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateVersionedClient().GetAsync("/api/v1/me/data", Ct)).StatusCode);
}
