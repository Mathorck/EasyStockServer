using System.Buffers.Text;
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
        _factory.Time.Advance(AuthLimitsHelper.RefreshReuseGrace + TimeSpan.FromSeconds(1));

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

    [Fact]
    public async Task Reusing_a_token_several_rotations_old_revokes_the_session()
    {
        var user = await _factory.CreateUserAsync();
        // A thief refreshes twice with the copied token: the phone's token is now two rotations behind.
        var t1 = await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();
        var t2 = await (await _factory.RefreshAsync(t1.RefreshToken)).Read<TokenAnswer>();

        await (await _factory.RefreshAsync(user.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        Assert.Equal(AuthLimitsHelper.RevokeReasons.TokenReuse, await RevokeReason(user.Id));
        await (await _factory.RefreshAsync(t2.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
    }

    [Fact]
    public async Task Replaying_the_just_rotated_token_within_the_grace_window_returns_the_same_token()
    {
        var user = await _factory.CreateUserAsync();
        var next = await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();
        _factory.Time.Advance(AuthLimitsHelper.RefreshReuseGrace - TimeSpan.FromSeconds(1));

        var again = await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();
        Assert.Equal(next.RefreshToken, again.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(again.AccessToken)).StatusCode);
        Assert.Null(await RevokeReason(user.Id));

        var third = await (await _factory.RefreshAsync(next.RefreshToken)).Read<TokenAnswer>();
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(third.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Parallel_refreshes_with_one_token_get_the_same_new_token()
    {
        var user = await _factory.CreateUserAsync();
        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>()));

        Assert.Single(answers.Select(a => a.RefreshToken).Distinct());
        Assert.NotEqual(user.RefreshToken, answers[0].RefreshToken);
        Assert.Null(await RevokeReason(user.Id));
        await (await _factory.RefreshAsync(answers[0].RefreshToken)).Read<TokenAnswer>();
    }

    [Fact]
    public async Task Forged_token_naming_a_real_session_does_not_revoke_it()
    {
        var user = await _factory.CreateUserAsync();
        var raw = Base64Url.DecodeFromChars(user.RefreshToken);
        raw[^1] ^= 0x01;

        await (await _factory.RefreshAsync(Base64Url.EncodeToString(raw))).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        Assert.Null(await RevokeReason(user.Id));
        await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();
    }

    [Fact]
    public async Task Token_of_the_older_random_format_still_refreshes()
    {
        var user = await _factory.CreateUserAsync();
        var legacy = SecretTokensHelper.New();
        var hash = SecretTokensHelper.Hash(legacy);
        await _factory.WithDb(db => db.DeviceSessions.Where(s => s.UserId == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RefreshTokenHash, hash), TestContext.Current.CancellationToken));

        var next = await (await _factory.RefreshAsync(legacy)).Read<TokenAnswer>();
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(next.AccessToken)).StatusCode);
        var third = await (await _factory.RefreshAsync(next.RefreshToken)).Read<TokenAnswer>();

        // Once rotated, the session's tokens name it: the old random token is now reuse.
        _factory.Time.Advance(AuthLimitsHelper.RefreshReuseGrace + TimeSpan.FromSeconds(1));
        await (await _factory.RefreshAsync(next.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        Assert.Equal(AuthLimitsHelper.RevokeReasons.TokenReuse, await RevokeReason(user.Id));
        await (await _factory.RefreshAsync(third.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
    }

    [Fact]
    public async Task Disabled_user_is_refused_within_the_grace_window()
    {
        var user = await _factory.CreateUserAsync();
        await (await _factory.RefreshAsync(user.RefreshToken)).Read<TokenAnswer>();
        var now = _factory.Time.GetUtcNow();
        await _factory.WithDb(db => db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.DisabledAt, now), TestContext.Current.CancellationToken));

        await (await _factory.RefreshAsync(user.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "account-disabled");
    }
}
