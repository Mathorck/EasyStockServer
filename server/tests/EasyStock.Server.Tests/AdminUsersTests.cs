using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>Each test has its own database: the last-Admin rule counts every Admin of the server.</summary>
public sealed class AdminUsersTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Base = "/api/v1/admin/users";

    private async Task<(ApiFactory Factory, TestUser Admin)> NewServer()
    {
        var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        return (factory, await factory.CreateUserAsync(Role.Admin));
    }

    private static HttpClient As(ApiFactory f, TestUser u) => f.CreateVersionedClient(u.AccessToken);

    private static Task<HttpResponseMessage> Patch(HttpClient c, Guid id, object body) =>
        c.PatchAsJsonAsync($"{Base}/{id}", body, Ct);

    [Fact]
    public async Task Only_admins_can_use_the_routes()
    {
        var (factory, _) = await NewServer();
        using var _f = factory;
        var member = await factory.CreateUserAsync(Role.Member);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(factory, member).GetAsync(Base, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateVersionedClient().GetAsync(Base, Ct)).StatusCode);
    }

    [Fact]
    public async Task List_shows_role_kind_devices_and_pending_promotion()
    {
        var (factory, admin) = await NewServer();
        using var _f = factory;
        var guest = await (await factory.GuestSignupAsync(await factory.EnableGuestSignupAsync())).Read<TokenAnswer>();
        var client = As(factory, admin);
        await (await client.PostJson($"{Base}/{guest.User.Id}/promote", new { role = "viewer", shortCode = guest.User.ShortCode }))
            .Read<PendingPromotionAnswer>();

        var list = await (await client.GetAsync(Base, Ct)).Read<List<AdminUserAnswer>>();
        var a = Assert.Single(list, u => u.Id == admin.Id);
        Assert.Equal(("admin", "full"), (a.Role, a.Kind));
        Assert.Single(a.Devices);
        var g = Assert.Single(list, u => u.Id == guest.User.Id);
        Assert.Equal("guest", g.Kind);
        Assert.Equal("viewer", g.PendingPromotion?.Role);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Base}/{guest.User.Id}/promote", Ct)).StatusCode);
        await (await client.DeleteAsync($"{Base}/{guest.User.Id}/promote", Ct)).Problem(HttpStatusCode.NotFound, "no-promotion");
        await (await client.PostJson($"{Base}/{guest.User.Id}/promote", new { role = "member", shortCode = "AAA-AAA" }))
            .Problem(HttpStatusCode.BadRequest, "short-code-mismatch");
        await (await client.PostJson($"{Base}/{guest.User.Id}/promote", new { role = "guest", shortCode = guest.User.ShortCode }))
            .Problem(HttpStatusCode.Conflict, "role-not-allowed");
        await (await client.PostJson($"{Base}/{guest.User.Id}/promote", new { role = "nope", shortCode = guest.User.ShortCode }))
            .Problem(HttpStatusCode.BadRequest, "validation-failed");
    }

    [Fact]
    public async Task Patch_changes_role_and_name_and_disable_revokes_sessions()
    {
        var (factory, admin) = await NewServer();
        using var _f = factory;
        var user = await factory.CreateUserAsync(Role.Member);
        var client = As(factory, admin);

        var changed = await (await Patch(client, user.Id, new { role = "viewer", displayName = " Jeanne " })).Read<AdminUserAnswer>();
        Assert.Equal(("viewer", "Jeanne"), (changed.Role, changed.DisplayName));
        Assert.Equal("viewer", (await (await factory.GetMeAsync(user.AccessToken)).Read<MeAnswer>()).Role);

        var disabled = await (await Patch(client, user.Id, new { disabled = true })).Read<AdminUserAnswer>();
        Assert.True(disabled.Disabled);
        Assert.Empty(disabled.Devices);
        await (await factory.GetMeAsync(user.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await factory.LoginAsync(user.Username, user.Password)).Problem(HttpStatusCode.Unauthorized, "account-disabled");

        await (await Patch(client, user.Id, new { disabled = false })).Read<AdminUserAnswer>();
        Assert.Equal(HttpStatusCode.OK, (await factory.LoginAsync(user.Username, user.Password)).StatusCode);

        await (await Patch(client, user.Id, new { role = "guest" })).Problem(HttpStatusCode.Conflict, "role-not-allowed");
        await (await Patch(client, user.Id, new { role = "boss" })).Problem(HttpStatusCode.BadRequest, "validation-failed");
        await (await Patch(client, user.Id, new { displayName = " " })).Problem(HttpStatusCode.BadRequest, "validation-failed");
        await (await Patch(client, Guid.NewGuid(), new { disabled = true })).Problem(HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Last_active_admin_is_protected()
    {
        var (factory, admin) = await NewServer();
        using var _f = factory;
        var client = As(factory, admin);
        await (await Patch(client, admin.Id, new { role = "member" })).Problem(HttpStatusCode.Conflict, "last-admin");
        await (await Patch(client, admin.Id, new { disabled = true })).Problem(HttpStatusCode.Conflict, "last-admin");
        await (await client.DeleteAsync($"{Base}/{admin.Id}", Ct)).Problem(HttpStatusCode.Conflict, "last-admin");

        // A disabled Admin does not count as active.
        var second = await factory.CreateUserAsync(Role.Admin);
        await (await Patch(client, second.Id, new { disabled = true })).Read<AdminUserAnswer>();
        await (await Patch(client, admin.Id, new { role = "member" })).Problem(HttpStatusCode.Conflict, "last-admin");
        await (await Patch(client, second.Id, new { disabled = false })).Read<AdminUserAnswer>();
        await (await Patch(client, admin.Id, new { role = "member" })).Read<AdminUserAnswer>();
    }

    [Fact]
    public async Task Delete_is_soft_keeps_the_row_and_frees_the_username()
    {
        var (factory, admin) = await NewServer();
        using var _f = factory;
        var user = await factory.CreateUserAsync(Role.Member);
        var client = As(factory, admin);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Base}/{user.Id}", Ct)).StatusCode);
        await (await factory.GetMeAsync(user.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await factory.LoginAsync(user.Username, user.Password)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.DoesNotContain((await (await client.GetAsync(Base, Ct)).Read<List<AdminUserAnswer>>()), u => u.Id == user.Id);
        await (await client.DeleteAsync($"{Base}/{user.Id}", Ct)).Problem(HttpStatusCode.NotFound, "not-found");
        await (await client.PostAsync($"{Base}/{user.Id}/reset", null, Ct)).Problem(HttpStatusCode.NotFound, "not-found");

        await factory.WithDb(async db =>
        {
            var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, Ct);
            Assert.Equal("Deleted user", row.DisplayName);
            Assert.NotNull(row.DeletedAt);
            Assert.Null(row.UserName);
        });
    }

    [Fact]
    public async Task Reset_returns_a_qr_and_the_token_works_once()
    {
        var (factory, admin) = await NewServer();
        using var _f = factory;
        var user = await factory.CreateUserAsync(Role.Member);
        var client = As(factory, admin);

        var grant = await (await client.PostAsync($"{Base}/{user.Id}/reset", null, Ct)).Read<QrGrant>();
        Assert.StartsWith("easystock://reset?", grant.Qr);
        var redeem = () => factory.CreateVersionedClient().PostJson("/api/v1/auth/reset",
            new { resetToken = grant.Token, password = "a brand new password", deviceName = "Pixel" });
        Assert.Equal(HttpStatusCode.OK, (await redeem()).StatusCode);
        await (await redeem()).Problem(HttpStatusCode.Gone, "reset-used");

        var guest = await (await factory.GuestSignupAsync(await factory.EnableGuestSignupAsync())).Read<TokenAnswer>();
        await (await client.PostAsync($"{Base}/{guest.User.Id}/reset", null, Ct)).Problem(HttpStatusCode.Conflict, "use-promotion");
    }

    private const string PushEndpoint = "https://ntfy.example.org/up-admin-test";

    private static async Task AddPushRegistrations(ApiFactory factory, params Guid[] sessionIds) =>
        await factory.WithDb(async db =>
        {
            foreach (var id in sessionIds)
                db.Set<PushRegistration>().Add(new PushRegistration { DeviceSessionId = id, Endpoint = PushEndpoint, CreatedAt = 1, UpdatedAt = 1 });
            await db.SaveChangesAsync(Ct);
        });

    private static async Task<T> Db<T>(ApiFactory factory, Func<EasyStockDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<EasyStockDbContext>());
    }

    private static Task<Guid> SessionOf(ApiFactory factory, Guid userId) =>
        Db(factory, db => db.DeviceSessions.Where(s => s.UserId == userId).Select(s => s.Id).SingleAsync(Ct));

    private static Task<bool> HasPush(ApiFactory factory, Guid sessionId) =>
        Db(factory, db => db.Set<PushRegistration>().AnyAsync(p => p.DeviceSessionId == sessionId, Ct));

    [Fact]
    public async Task Disabling_a_user_removes_the_push_registrations_of_their_sessions_but_keeps_others()
    {
        var (factory, admin) = await NewServer();
        using var _f = factory;
        var user = await factory.CreateUserAsync(Role.Member);
        var bystander = await factory.SignInAsync(Role.Member);
        var userSession = await SessionOf(factory, user.Id);
        await AddPushRegistrations(factory, userSession, bystander.SessionId);

        await (await Patch(As(factory, admin), user.Id, new { disabled = true })).Read<AdminUserAnswer>();

        Assert.False(await HasPush(factory, userSession));
        Assert.True(await HasPush(factory, bystander.SessionId));
    }

    [Fact]
    public async Task Deleting_a_user_removes_the_push_registrations_of_their_sessions()
    {
        var (factory, admin) = await NewServer();
        using var _f = factory;
        var user = await factory.CreateUserAsync(Role.Member);
        var userSession = await SessionOf(factory, user.Id);
        await AddPushRegistrations(factory, userSession);

        Assert.Equal(HttpStatusCode.NoContent, (await As(factory, admin).DeleteAsync($"{Base}/{user.Id}", Ct)).StatusCode);

        Assert.False(await HasPush(factory, userSession));
        Assert.True(await Db(factory, db => db.DeviceSessions.AnyAsync(s => s.Id == userSession && s.RevokedAt != null, Ct)));
    }

    [Fact]
    public async Task A_deleted_user_is_named_Deleted_user_in_the_pull_and_still_authors_their_movements()
    {
        var (factory, admin) = await NewServer();
        using var _f = factory;
        var author = await factory.CreateUserAsync(Role.Member);
        var reader = await factory.SignInAsync(Role.Member);
        var category = new Category { Id = Guid.NewGuid(), Name = "cat", CreatedAt = 1, UpdatedAt = 1 };
        var location = new Location { Id = Guid.NewGuid(), Name = "loc", CreatedAt = 1, UpdatedAt = 1 };
        var article = new Article { Id = Guid.NewGuid(), Name = "art", CategoryId = category.Id, Threshold = 0, Attributes = "{}", CreatedAt = 1, UpdatedAt = 1 };
        var movement = new StockMovement
        {
            Id = Guid.NewGuid(), ArticleId = article.Id, LocationId = location.Id, Quantity = 1000, Reason = "consumption",
            OccurredAt = 1, CreatedAt = 1, CreatedBy = author.Id, DeviceId = Guid.NewGuid(),
        };
        await factory.WithDb(async db =>
        {
            foreach (object row in new object[] { category, location, article, movement })
            {
                db.Add(row);
                await db.SaveChangesAsync(Ct);
            }
        });

        Assert.Equal(HttpStatusCode.NoContent, (await As(factory, admin).DeleteAsync($"{Base}/{author.Id}", Ct)).StatusCode);

        var response = await factory.ClientFor(reader).GetAsync("/api/v1/sync/pull?since=0&limit=2000", Ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var users = doc.RootElement.GetProperty("users").EnumerateArray()
            .ToDictionary(u => u.GetProperty("id").GetString()!, u => u.GetProperty("displayName").GetString()!);
        Assert.Equal("Deleted user", users[author.Id.ToString()]);
        var row = doc.RootElement.GetProperty("rows").GetProperty("stock_movement").EnumerateArray().Single();
        Assert.Equal(author.Id.ToString(), row.GetProperty("createdBy").GetString());
    }
}
