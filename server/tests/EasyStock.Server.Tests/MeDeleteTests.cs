using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>
/// <c>DELETE /me</c>: self-service account deletion with erasure (nFADP / GDPR). Each test has its own database: the
/// last-Admin rule counts every Admin of the server.
/// </summary>
public sealed class MeDeleteTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ApiFactory> NewServer()
    {
        var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        // An Admin, so a Member or Guest is never the last one.
        await factory.CreateUserAsync(Role.Admin);
        return factory;
    }

    private static Task<HttpResponseMessage> DeleteMe(ApiFactory f, string accessToken, object? body, string? ip = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me");
        if (body is not null) request.Content = JsonContent.Create(body);
        return f.CreateVersionedClient(accessToken, ip).SendAsync(request, Ct);
    }

    [Fact]
    public async Task Correct_password_deletes_and_erases_every_personal_field()
    {
        using var factory = await NewServer();
        var user = await factory.CreateUserAsync(Role.Member);
        await factory.CreateVersionedClient(user.AccessToken)
            .PatchAsJsonAsync("/api/v1/me", new { alertsEnabled = false }, Ct);
        var sessionId = Guid.Empty;
        await factory.WithDb(async db =>
        {
            sessionId = await db.DeviceSessions.Where(s => s.UserId == user.Id).Select(s => s.Id).SingleAsync(Ct);
            db.Set<PushRegistration>().Add(new PushRegistration
            {
                DeviceSessionId = sessionId, Endpoint = "https://ntfy.example.org/upSECRET", CreatedAt = 1, UpdatedAt = 1,
            });
            db.PasswordResets.Add(new PasswordReset
            {
                Id = Guid.NewGuid(), TokenHash = SecretTokensHelper.Hash("reset-token"), UserId = user.Id,
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            });
            // A second, already revoked session: its device name must go too.
            db.DeviceSessions.Add(new DeviceSession
            {
                Id = Guid.NewGuid(), UserId = user.Id, DeviceName = "Old phone",
                RefreshTokenHash = SecretTokensHelper.Hash(Guid.NewGuid().ToString()),
                CreatedAt = DateTimeOffset.UtcNow, LastUsedAt = DateTimeOffset.UtcNow,
                RevokedAt = DateTimeOffset.UtcNow, RevokeReason = "logout",
            });
            await db.SaveChangesAsync(Ct);
        });

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteMe(factory, user.AccessToken, new { password = user.Password })).StatusCode);

        await factory.WithDb(async db =>
        {
            var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, Ct);
            Assert.NotNull(row.DeletedAt);
            Assert.Equal("Deleted user", row.DisplayName);
            Assert.Null(row.UserName);
            Assert.Null(row.NormalizedUserName);
            Assert.Null(row.PasswordHash);
            Assert.Null(row.AlertsEnabled);
            Assert.Null(row.LastSeenAt);
            var sessions = await db.DeviceSessions.AsNoTracking().Where(s => s.UserId == user.Id).ToListAsync(Ct);
            Assert.Equal(2, sessions.Count);
            Assert.All(sessions, s =>
            {
                Assert.Equal(AccountsHelper.DeletedDeviceName, s.DeviceName);
                Assert.NotNull(s.RevokedAt);
            });
            Assert.Equal("deleted", sessions.Single(s => s.Id == sessionId).RevokeReason);
            Assert.False(await db.Set<PushRegistration>().AnyAsync(p => p.DeviceSessionId == sessionId, Ct));
            Assert.False(await db.PasswordResets.AnyAsync(r => r.UserId == user.Id, Ct));
        });

        // Sessions revoked: the old access and refresh tokens are refused, the password no longer logs in.
        await (await factory.GetMeAsync(user.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await factory.RefreshAsync(user.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await factory.LoginAsync(user.Username, user.Password)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");

        // The username is free again.
        var invite = await factory.CreateInviteAsync(Role.Viewer);
        var redeem = await factory.CreateVersionedClient().PostJson("/api/v1/auth/invite/redeem",
            new { inviteToken = invite.Token, username = user.Username, displayName = "Someone else", password = AuthHelpers.Password, deviceName = "Pixel" });
        Assert.NotEqual(user.Id, (await redeem.Read<TokenAnswer>()).User.Id);
    }

    [Fact]
    public async Task Wrong_or_missing_password_is_refused_and_keeps_the_account()
    {
        using var factory = await NewServer();
        var user = await factory.CreateUserAsync(Role.Member);

        await (await DeleteMe(factory, user.AccessToken, new { password = "wrong password" }))
            .Problem(HttpStatusCode.BadRequest, "invalid-credentials");
        await (await DeleteMe(factory, user.AccessToken, new { })).Problem(HttpStatusCode.BadRequest, "invalid-credentials");
        await (await DeleteMe(factory, user.AccessToken, null)).Problem(HttpStatusCode.BadRequest, "invalid-credentials");

        Assert.Equal(HttpStatusCode.OK, (await factory.GetMeAsync(user.AccessToken)).StatusCode);
        await factory.WithDb(async db =>
            Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, Ct)).DeletedAt));
    }

    [Fact]
    public async Task Wrong_passwords_share_the_login_lockout()
    {
        using var factory = await NewServer();
        var user = await factory.CreateUserAsync(Role.Member);
        var ip = ApiFactory.RandomIp();

        for (var i = 0; i < 4; i++)
            await (await DeleteMe(factory, user.AccessToken, new { password = "wrong password" }, ip))
                .Problem(HttpStatusCode.BadRequest, "invalid-credentials");
        await (await factory.LoginAsync(user.Username, "wrong password", ip)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");

        var locked = await (await DeleteMe(factory, user.AccessToken, new { password = user.Password }, ip))
            .Problem(HttpStatusCode.TooManyRequests, "rate-limited");
        Assert.True(locked.GetProperty("retryAfter").GetInt32() > 0);

        factory.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteMe(factory, user.AccessToken, new { password = user.Password }, ip)).StatusCode);
    }

    [Fact]
    public async Task A_guest_without_a_password_deletes_without_a_body_and_loses_the_pending_promotion()
    {
        using var factory = await NewServer();
        var guest = await factory.SignInAsync(Role.Guest);
        await factory.WithDb(async db =>
        {
            db.Promotions.Add(new Promotion
            {
                Id = Guid.NewGuid(), UserId = guest.UserId, TargetRole = Role.Member,
                CreatedAt = factory.Time.GetUtcNow(), ExpiresAt = factory.Time.GetUtcNow().AddDays(1),
            });
            await db.SaveChangesAsync(Ct);
        });

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteMe(factory, guest.AccessToken, null)).StatusCode);

        await factory.WithDb(async db =>
        {
            var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == guest.UserId, Ct);
            Assert.NotNull(row.DeletedAt);
            Assert.Equal("Deleted user", row.DisplayName);
            Assert.Equal(AccountsHelper.DeletedDeviceName,
                await db.DeviceSessions.Where(s => s.Id == guest.SessionId).Select(s => s.DeviceName).SingleAsync(Ct));
            var promotion = await db.Promotions.AsNoTracking().SingleAsync(p => p.UserId == guest.UserId, Ct);
            Assert.Equal(Promotion.Cancelled, promotion.EndReason);
        });
        await (await factory.GetMeAsync(guest.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
    }

    [Fact]
    public async Task A_guest_may_also_send_a_body()
    {
        using var factory = await NewServer();
        var guest = await factory.SignInAsync(Role.Guest);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteMe(factory, guest.AccessToken, new { password = "anything" })).StatusCode);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_delete_themselves()
    {
        using var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        var admin = await factory.CreateUserAsync(Role.Admin);

        await (await DeleteMe(factory, admin.AccessToken, new { password = admin.Password }))
            .Problem(HttpStatusCode.Conflict, "last-admin");
        Assert.Equal(HttpStatusCode.OK, (await factory.GetMeAsync(admin.AccessToken)).StatusCode);

        await factory.CreateUserAsync(Role.Admin);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteMe(factory, admin.AccessToken, new { password = admin.Password })).StatusCode);
    }

    [Fact]
    public async Task Other_users_pull_Deleted_user_and_the_movements_are_kept()
    {
        using var factory = await NewServer();
        var author = await factory.CreateUserAsync(Role.Member);
        var reader = await factory.SignInAsync(Role.Viewer);
        var category = new Category { Id = Guid.NewGuid(), Name = "cat", CreatedAt = 1, UpdatedAt = 1 };
        var location = new Location { Id = Guid.NewGuid(), Name = "loc", CreatedAt = 1, UpdatedAt = 1 };
        var article = new Article { Id = Guid.NewGuid(), Name = "art", CategoryId = category.Id, Threshold = 0, Attributes = "{}", CreatedAt = 1, UpdatedAt = 1 };
        var movement = new StockMovement
        {
            Id = Guid.NewGuid(), ArticleId = article.Id, LocationId = location.Id, Quantity = 1500, Reason = "consumption",
            Comment = "mine", OccurredAt = 1, CreatedAt = 1, CreatedBy = author.Id, DeviceId = Guid.NewGuid(),
        };
        await factory.WithDb(async db =>
        {
            foreach (object row in new object[] { category, location, article, movement })
            {
                db.Add(row);
                await db.SaveChangesAsync(Ct);
            }
        });
        StockMovement before = null!;
        await factory.WithDb(async db => before = await db.StockMovements.AsNoTracking().SingleAsync(m => m.Id == movement.Id, Ct));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteMe(factory, author.AccessToken, new { password = author.Password })).StatusCode);

        await factory.WithDb(async db =>
        {
            var after = await db.StockMovements.AsNoTracking().SingleAsync(m => m.Id == movement.Id, Ct);
            // Never updated: same server_seq, author, device and content.
            Assert.Equal(before.ServerSeq, after.ServerSeq);
            Assert.Equal(author.Id, after.CreatedBy);
            Assert.Equal(before.DeviceId, after.DeviceId);
            Assert.Equal(1500, after.Quantity);
            Assert.Equal("mine", after.Comment);
        });

        var response = await factory.ClientFor(reader).GetAsync("/api/v1/sync/pull?since=0&limit=2000", Ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var users = doc.RootElement.GetProperty("users").EnumerateArray()
            .ToDictionary(u => u.GetProperty("id").GetString()!, u => u.GetProperty("displayName").GetString()!);
        Assert.Equal("Deleted user", users[author.Id.ToString()]);
        var row = doc.RootElement.GetProperty("rows").GetProperty("stock_movement").EnumerateArray().Single();
        Assert.Equal(author.Id.ToString(), row.GetProperty("createdBy").GetString());
    }

    [Fact]
    public async Task Delete_needs_a_valid_token()
    {
        using var factory = await NewServer();
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateVersionedClient().SendAsync(request, Ct)).StatusCode);
    }
}
