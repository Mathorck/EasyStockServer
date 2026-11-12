using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class AdminDevicesTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<AdminDeviceAnswer>> ListAsync(string accessToken) =>
        await (await _factory.CreateVersionedClient(accessToken).GetAsync("/api/v1/admin/devices", Ct))
            .Read<List<AdminDeviceAnswer>>();

    private Task<HttpResponseMessage> RevokeAsync(string accessToken, Guid id) =>
        _factory.CreateVersionedClient(accessToken).DeleteAsync($"/api/v1/admin/devices/{id}", Ct);

    /// <summary>The session id is the <c>sid</c> claim of the access token (the payload of the JWT).</summary>
    private static Guid SessionIdOf(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return Guid.Parse(json.RootElement.GetProperty(AccessTokenHandler.SessionClaim).GetString()!);
    }

    [Fact]
    public async Task Lists_every_session_with_its_user_and_revoked_state()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);
        var member = await _factory.CreateUserAsync(Role.Member);
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(member.Username, AuthHelpers.Password)).StatusCode);

        var devices = await ListAsync(admin.AccessToken);
        var mine = devices.Where(d => d.UserId == member.Id).ToList();
        Assert.Equal(2, mine.Count);
        Assert.All(mine, d =>
        {
            Assert.Equal("Test user", d.UserDisplayName);
            Assert.Equal("Pixel", d.DeviceName);
            Assert.Null(d.RevokedAt);
            Assert.Null(d.RevokeReason);
        });
        Assert.Contains(devices, d => d.UserId == admin.Id);
    }

    [Fact]
    public async Task Only_administrators_can_list_or_revoke()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);
        var member = await _factory.CreateUserAsync(Role.Member);
        var target = (await ListAsync(admin.AccessToken)).Single(d => d.UserId == member.Id);

        var asMember = _factory.CreateVersionedClient(member.AccessToken);
        await (await asMember.GetAsync("/api/v1/admin/devices", Ct)).Problem(HttpStatusCode.Forbidden, "not-allowed");
        await (await RevokeAsync(member.AccessToken, target.Id)).Problem(HttpStatusCode.Forbidden, "not-allowed");

        using var anonymous = _factory.CreateVersionedClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/devices", Ct)).StatusCode);

        // The refused revoke changed nothing.
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(member.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Revoke_ends_access_and_refresh_of_that_session_only_and_is_idempotent()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);
        var member = await _factory.CreateUserAsync(Role.Member);
        var keep = await (await _factory.LoginAsync(member.Username, AuthHelpers.Password)).Read<TokenAnswer>();
        var targetId = SessionIdOf(member.AccessToken);
        Assert.NotEqual(targetId, SessionIdOf(keep.AccessToken));

        Assert.Equal(HttpStatusCode.NoContent, (await RevokeAsync(admin.AccessToken, targetId)).StatusCode);

        // Access token of the revoked session is refused at once.
        await (await _factory.GetMeAsync(member.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        // Its refresh token is refused too.
        await (await _factory.RefreshAsync(member.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");

        // The other session of the same user keeps working.
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(keep.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.RefreshAsync(keep.RefreshToken)).StatusCode);

        var after = (await ListAsync(admin.AccessToken)).Single(d => d.Id == targetId);
        Assert.NotNull(after.RevokedAt);
        Assert.Equal("admin", after.RevokeReason);

        // Idempotent: a second revoke, a minute later, answers 204 and keeps the first time and reason.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.NoContent, (await RevokeAsync(admin.AccessToken, targetId)).StatusCode);
        var again = (await ListAsync(admin.AccessToken)).Single(d => d.Id == targetId);
        Assert.Equal(after.RevokedAt, again.RevokedAt);
        Assert.Equal("admin", again.RevokeReason);
    }

    [Fact]
    public async Task Admin_revoke_keeps_the_reason_of_a_session_already_ended()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);
        var member = await _factory.CreateUserAsync(Role.Member);
        var targetId = SessionIdOf(member.AccessToken);
        await _factory.WithService<bool, SessionHandler>(s => s.RevokeAsync(targetId, "logout", Ct));
        var ended = (await ListAsync(admin.AccessToken)).Single(d => d.Id == targetId);

        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.NoContent, (await RevokeAsync(admin.AccessToken, targetId)).StatusCode);

        var after = (await ListAsync(admin.AccessToken)).Single(d => d.Id == targetId);
        Assert.Equal(ended.RevokedAt, after.RevokedAt);
        Assert.Equal("logout", after.RevokeReason);
    }

    [Fact]
    public async Task Unknown_session_is_404_not_found()
    {
        var admin = await _factory.CreateUserAsync(Role.Admin);
        await (await RevokeAsync(admin.AccessToken, Guid.NewGuid())).Problem(HttpStatusCode.NotFound, "not-found");
    }
}
