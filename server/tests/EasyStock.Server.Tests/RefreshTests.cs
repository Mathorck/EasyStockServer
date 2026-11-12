using System.Net;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class RefreshTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private async Task<string?> RevokeReason(Guid userId)
    {
        string? reason = null;
        await _factory.WithDb(async db => reason = await db.DeviceSessions.Where(s => s.UserId == userId)
            .Select(s => s.RevokeReason).SingleAsync(TestContext.Current.CancellationToken));
        return reason;
    }

    [Fact]
    public async Task Refresh_rotates_the_tokens()
    {
        var user = await _factory.CreateUserAsync();
        var next = await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();
        Assert.NotEqual(user.RefreshToken, next.RefreshToken);
        Assert.Equal(user.Id, next.User.Id);
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(next.AccessToken)).StatusCode);

        var third = await (await _factory.RefreshAsync(next.RefreshToken)).Read<TokenAnswer>();
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(third.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Reusing_the_previous_token_revokes_the_session()
    {
        var user = await _factory.CreateUserAsync();
        var next = await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();

        await (await _factory.RefreshAsync(user.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        Assert.Equal(AuthLimitsHelper.RevokeReasons.TokenReuse, await RevokeReason(user.Id));

        await (await _factory.RefreshAsync(next.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await _factory.GetMeAsync(next.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
    }

    [Fact]
    public async Task Unknown_token_is_refused()
    {
        await (await _factory.RefreshAsync(SecretTokensHelper.New())).Problem(HttpStatusCode.Unauthorized, "session-revoked");
    }

    [Fact]
    public async Task Revoked_session_is_refused()
    {
        var user = await _factory.CreateUserAsync();
        var logout = await _factory.CreateVersionedClient(user.AccessToken).PostAsync("/api/v1/auth/logout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(AuthLimitsHelper.RevokeReasons.Logout, await RevokeReason(user.Id));

        await (await _factory.RefreshAsync(user.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await _factory.GetMeAsync(user.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
    }

    [Fact]
    public async Task Access_token_expires_after_15_minutes()
    {
        var user = await _factory.CreateUserAsync();
        _factory.Time.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(user.AccessToken)).StatusCode);
        _factory.Time.Advance(TimeSpan.FromMinutes(2));
        await (await _factory.GetMeAsync(user.AccessToken)).Problem(HttpStatusCode.Unauthorized, "token-expired");

        var next = await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(next.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Session_unused_for_90_days_ends()
    {
        var user = await _factory.CreateUserAsync();
        _factory.Time.Advance(TimeSpan.FromDays(89));
        var next = await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();

        _factory.Time.Advance(TimeSpan.FromDays(90) + TimeSpan.FromMinutes(1));
        await (await _factory.RefreshAsync(next.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        Assert.Equal(AuthLimitsHelper.RevokeReasons.Expired, await RevokeReason(user.Id));
    }
}
