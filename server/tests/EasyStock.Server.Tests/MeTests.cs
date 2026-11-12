using System.Net.Http.Json;
using System.Net;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class MeTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> Patch(string accessToken, string json) =>
        _factory.CreateVersionedClient(accessToken).PatchAsync("/api/v1/me",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"), Ct);

    [Fact]
    public async Task Get_returns_the_account()
    {
        var user = await _factory.CreateUserAsync(Role.Viewer);
        var me = await (await _factory.GetMeAsync(user.AccessToken)).Read<MeAnswer>();
        Assert.Equal(user.Id, me.Id);
        Assert.Equal("Test user", me.DisplayName);
        Assert.Equal("viewer", me.Role);
        Assert.Equal("full", me.Kind);
        Assert.Equal(user.Tokens.User.ShortCode, me.ShortCode);
        Assert.Null(me.AlertsEnabled);
        Assert.Null(me.PendingPromotion);
    }

    [Fact]
    public async Task Patch_changes_only_the_fields_sent()
    {
        var user = await _factory.CreateUserAsync();
        var me = await (await Patch(user.AccessToken, """{"displayName":"  Alice  "}""")).Read<MeAnswer>();
        Assert.Equal("Alice", me.DisplayName);

        me = await (await Patch(user.AccessToken, """{"alertsEnabled":false}""")).Read<MeAnswer>();
        Assert.False(me.AlertsEnabled);
        Assert.Equal("Alice", me.DisplayName);

        me = await (await Patch(user.AccessToken, """{"displayName":"Bob"}""")).Read<MeAnswer>();
        Assert.False(me.AlertsEnabled);

        me = await (await Patch(user.AccessToken, """{"alertsEnabled":null}""")).Read<MeAnswer>();
        Assert.Null(me.AlertsEnabled);

        await (await Patch(user.AccessToken, """{"displayName":"   "}""")).Problem(HttpStatusCode.BadRequest, "validation-failed");
        await (await Patch(user.AccessToken, $$"""{"displayName":"{{new string('x', 51)}}"}""")).Problem(HttpStatusCode.BadRequest, "validation-failed");
    }

    [Fact]
    public async Task Change_password_ends_the_other_sessions()
    {
        var user = await _factory.CreateUserAsync();
        var other = await (await _factory.LoginAsync(user.Username, user.Password)).Read<TokenAnswer>();
        var client = _factory.CreateVersionedClient(user.AccessToken);

        await (await client.PostAsJsonAsync("/api/v1/me/password", new { currentPassword = "wrong password", newPassword = "another password" }, Ct))
            .Problem(HttpStatusCode.BadRequest, "invalid-credentials");
        await (await client.PostAsJsonAsync("/api/v1/me/password", new { currentPassword = user.Password, newPassword = "short" }, Ct))
            .Problem(HttpStatusCode.BadRequest, "validation-failed");

        var ok = await client.PostAsJsonAsync("/api/v1/me/password", new { currentPassword = user.Password, newPassword = "another password" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        await (await _factory.GetMeAsync(other.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await _factory.RefreshAsync(other.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(user.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.RefreshAsync(user.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, "another password")).StatusCode);
    }

    [Fact]
    public async Task Me_needs_a_valid_token()
    {
        var anonymous = await _factory.CreateVersionedClient().GetAsync("/api/v1/me", Ct);
        await anonymous.Problem(HttpStatusCode.Unauthorized, "unauthorized");
        Assert.Contains(anonymous.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");

        await (await _factory.GetMeAsync("not.a.token")).Problem(HttpStatusCode.Unauthorized, "unauthorized");

        // A token signed by another server's key.
        using var otherServer = new ApiFactory(pg.ConnectionString);
        var foreign = await otherServer.CreateUserAsync();
        await (await _factory.GetMeAsync(foreign.AccessToken)).Problem(HttpStatusCode.Unauthorized, "unauthorized");
    }
}
