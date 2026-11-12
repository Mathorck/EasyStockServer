using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Helpers;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>
/// The Admin endpoints of the printed Guest QR code, and the server-info flags that depend on it. Each test has
/// its own database: the public QR code and the user table are server-wide.
/// </summary>
public sealed class AdminPublicInviteTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Path = "/api/v1/admin/public-invite";

    private async Task<ApiFactory> NewServer()
    {
        var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        factory.CreateVersionedClient();
        return factory;
    }

    private static HttpClient AsAdmin(ApiFactory factory, TestUser admin) =>
        factory.CreateVersionedClient(admin.AccessToken);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task Every_route_needs_an_admin()
    {
        using var factory = await NewServer();
        var member = await factory.CreateUserAsync(Role.Member);
        var viewer = await factory.CreateUserAsync(Role.Viewer);
        var guest = await (await factory.GuestSignupAsync(await factory.EnableGuestSignupAsync())).Read<TokenAnswer>();

        var anonymous = factory.CreateVersionedClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Path, new { enabled = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(Path + "/regenerate", null, Ct)).StatusCode);

        foreach (var token in new[] { member.AccessToken, viewer.AccessToken, guest.AccessToken })
        {
            var client = factory.CreateVersionedClient(token);
            await (await client.GetAsync(Path, Ct)).Problem(HttpStatusCode.Forbidden, "not-allowed");
            await (await client.PutAsJsonAsync(Path, new { enabled = true }, Ct)).Problem(HttpStatusCode.Forbidden, "not-allowed");
            await (await client.PostAsync(Path + "/regenerate", null, Ct)).Problem(HttpStatusCode.Forbidden, "not-allowed");
        }
    }

    [Fact]
    public async Task Fresh_server_is_off_with_the_default_limit_and_no_code()
    {
        using var factory = await NewServer();
        var admin = await factory.CreateUserAsync(Role.Admin);

        var state = await ReadJson(await AsAdmin(factory, admin).GetAsync(Path, Ct));
        Assert.False(state.GetProperty("enabled").GetBoolean());
        Assert.Equal(10, state.GetProperty("guestLimit").GetInt32());
        Assert.Equal(0, state.GetProperty("guestCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("createdAt").ValueKind);
        Assert.False(state.TryGetProperty("token", out _) && state.GetProperty("token").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Put_turns_it_on_and_sets_the_limit()
    {
        using var factory = await NewServer();
        var admin = await factory.CreateUserAsync(Role.Admin);
        var client = AsAdmin(factory, admin);

        var put = await ReadJson(await client.PutAsJsonAsync(Path, new { enabled = true, guestLimit = 3 }, Ct));
        Assert.True(put.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, put.GetProperty("guestLimit").GetInt32());

        var get = await ReadJson(await client.GetAsync(Path, Ct));
        Assert.True(get.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, get.GetProperty("guestLimit").GetInt32());

        // Absent guestLimit means no limit (PUT replaces the whole state).
        var unlimited = await ReadJson(await client.PutAsJsonAsync(Path, new { enabled = true }, Ct));
        Assert.Equal(JsonValueKind.Null, unlimited.GetProperty("guestLimit").ValueKind);
    }

    [Fact]
    public async Task Invalid_put_is_a_validation_failure()
    {
        using var factory = await NewServer();
        var admin = await factory.CreateUserAsync(Role.Admin);
        var client = AsAdmin(factory, admin);

        var negative = await (await client.PutAsJsonAsync(Path, new { enabled = true, guestLimit = -1 }, Ct))
            .Problem(HttpStatusCode.BadRequest, "validation-failed");
        Assert.True(negative.GetProperty("errors").TryGetProperty("guestLimit", out _));

        await (await client.PutAsJsonAsync(Path, new { guestLimit = 5 }, Ct))
            .Problem(HttpStatusCode.BadRequest, "validation-failed");

        // Nothing changed.
        var state = await ReadJson(await client.GetAsync(Path, Ct));
        Assert.False(state.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Regenerate_returns_the_new_qr_and_old_codes_stop_working()
    {
        using var factory = await NewServer();
        var admin = await factory.CreateUserAsync(Role.Admin);
        var client = AsAdmin(factory, admin);
        await client.PutAsJsonAsync(Path, new { enabled = true }, Ct);

        var first = await ReadJson(await client.PostAsync(Path + "/regenerate", null, Ct));
        var oldToken = first.GetProperty("token").GetString()!;
        var second = await ReadJson(await client.PostAsync(Path + "/regenerate", null, Ct));
        var newToken = second.GetProperty("token").GetString()!;
        var qr = second.GetProperty("qr").GetString()!;
        Assert.NotEqual(oldToken, newToken);
        Assert.StartsWith("easystock://guest?server=https%3A%2F%2Fstock.example.org&token=", qr);
        Assert.Contains(Uri.EscapeDataString(newToken), qr);

        // A later GET never shows the token again.
        var state = await ReadJson(await client.GetAsync(Path, Ct));
        Assert.False(state.TryGetProperty("token", out var leaked) && leaked.ValueKind != JsonValueKind.Null);

        await (await factory.GuestSignupAsync(oldToken)).Problem(HttpStatusCode.NotFound, "invite-invalid");
        Assert.Equal(HttpStatusCode.OK, (await factory.GuestSignupAsync(newToken)).StatusCode);
    }

    [Fact]
    public async Task Server_info_reports_setup_and_guest_signup_state()
    {
        using var factory = await NewServer();
        var anonymous = factory.CreateClient();

        var before = await ReadJson(await anonymous.GetAsync("/api/v1/server-info", Ct));
        Assert.False(before.GetProperty("setupDone").GetBoolean());
        Assert.False(before.GetProperty("guestSignupEnabled").GetBoolean());

        var admin = await factory.CreateUserAsync(Role.Admin);
        await AsAdmin(factory, admin).PutAsJsonAsync(Path, new { enabled = true }, Ct);

        // On, but no code has been generated yet: a signup would fail, so the app is told it is off.
        var noCode = await ReadJson(await anonymous.GetAsync("/api/v1/server-info", Ct));
        Assert.True(noCode.GetProperty("setupDone").GetBoolean());
        Assert.False(noCode.GetProperty("guestSignupEnabled").GetBoolean());

        await AsAdmin(factory, admin).PostAsync(Path + "/regenerate", null, Ct);

        var after = await ReadJson(await anonymous.GetAsync("/api/v1/server-info", Ct));
        Assert.True(after.GetProperty("setupDone").GetBoolean());
        Assert.True(after.GetProperty("guestSignupEnabled").GetBoolean());
    }

    [Fact]
    public async Task Server_info_guest_signup_is_off_again_when_the_code_is_switched_off()
    {
        using var factory = await NewServer();
        var admin = await factory.CreateUserAsync(Role.Admin);
        await factory.EnableGuestSignupAsync();
        var anonymous = factory.CreateClient();
        Assert.True((await ReadJson(await anonymous.GetAsync("/api/v1/server-info", Ct))).GetProperty("guestSignupEnabled").GetBoolean());

        await AsAdmin(factory, admin).PutAsJsonAsync(Path, new { enabled = false }, Ct);

        Assert.False((await ReadJson(await anonymous.GetAsync("/api/v1/server-info", Ct))).GetProperty("guestSignupEnabled").GetBoolean());
    }

    [Fact]
    public async Task Server_info_has_data_follows_the_nine_shared_tables()
    {
        using var factory = await NewServer();
        var anonymous = factory.CreateClient();
        var info = async () => (await ReadJson(await anonymous.GetAsync("/api/v1/server-info", Ct))).GetProperty("hasData").GetBoolean();

        Assert.False(await info());

        // Users are not shared data: an account alone does not make the server "have data".
        await factory.CreateUserAsync(Role.Admin);
        Assert.False(await info());

        await factory.WithDb(async db =>
        {
            db.Locations.Add(new DB.Entities.Location { Id = Guid.NewGuid(), Name = "shelf", CreatedAt = 1, UpdatedAt = 1, DeletedAt = 2 });
            await db.SaveChangesAsync(Ct);
        });
        Assert.True(await info());
    }
}
