using System.Net;
using EasyStock.Server.Configuration;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class ResetTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "a brand new password";

    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<QrGrant> CreateReset(Guid userId) =>
        _factory.WithService<QrGrant, PasswordResetHandler>(s => s.CreateAsync(userId, null, Ct));

    private Task<HttpResponseMessage> Redeem(string? token) =>
        _factory.CreateVersionedClient().PostJson("/api/v1/auth/reset", new { resetToken = token, password = NewPassword, deviceName = "Pixel" });

    [Fact]
    public async Task Reset_sets_the_password_and_ends_every_other_session()
    {
        var user = await _factory.CreateUserAsync();
        var second = await (await _factory.LoginAsync(user.Username, user.Password)).Read<TokenAnswer>();
        // A locked account can still be reset.
        for (var i = 0; i < AuthLimitsHelper.MaxFailedLogins; i++) await _factory.LoginAsync(user.Username, "wrong password");

        var grant = await CreateReset(user.Id);
        Assert.StartsWith("easystock://reset?", grant.Qr);
        var answer = await (await Redeem(grant.Token)).Read<TokenAnswer>();
        Assert.Equal(user.Id, answer.User.Id);
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetMeAsync(answer.AccessToken)).StatusCode);

        await (await _factory.RefreshAsync(user.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await _factory.RefreshAsync(second.RefreshToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");
        await (await _factory.GetMeAsync(second.AccessToken)).Problem(HttpStatusCode.Unauthorized, "session-revoked");

        await (await _factory.LoginAsync(user.Username, user.Password)).Problem(HttpStatusCode.Unauthorized, "invalid-credentials");
        Assert.Equal(HttpStatusCode.OK, (await _factory.LoginAsync(user.Username, NewPassword)).StatusCode);

        await (await Redeem(grant.Token)).Problem(HttpStatusCode.Gone, "reset-used");
    }

    [Fact]
    public async Task Reset_expires_after_24_hours()
    {
        var user = await _factory.CreateUserAsync();
        var grant = await CreateReset(user.Id);
        _factory.Time.Advance(AuthLimitsHelper.ResetLifetime + TimeSpan.FromMinutes(1));
        await (await Redeem(grant.Token)).Problem(HttpStatusCode.Gone, "reset-expired");
    }

    [Fact]
    public async Task A_new_reset_replaces_the_previous_one()
    {
        var user = await _factory.CreateUserAsync();
        var first = await CreateReset(user.Id);
        var second = await CreateReset(user.Id);
        await (await Redeem(first.Token)).Problem(HttpStatusCode.NotFound, "invite-invalid");
        Assert.Equal(HttpStatusCode.OK, (await Redeem(second.Token)).StatusCode);
    }

    [Fact]
    public async Task Unknown_user_or_token()
    {
        await (await Redeem(SecretTokensHelper.New())).Problem(HttpStatusCode.NotFound, "invite-invalid");
        var ex = await Assert.ThrowsAsync<ProblemException>(() => CreateReset(Guid.NewGuid()));
        Assert.Equal(ProblemTypesHelper.NotFound, ex.Type);
    }

    [Fact]
    public async Task Reset_token_still_works_after_a_rejected_new_password()
    {
        var user = await _factory.CreateUserAsync();
        var grant = await CreateReset(user.Id);

        var rejected = await _factory.CreateVersionedClient().PostJson("/api/v1/auth/reset",
            new { resetToken = grant.Token, password = "short", deviceName = "Pixel" });
        var problem = await rejected.Problem(HttpStatusCode.BadRequest, "validation-failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("password", out _));

        Assert.Equal(HttpStatusCode.OK, (await Redeem(grant.Token)).StatusCode);
        await (await Redeem(grant.Token)).Problem(HttpStatusCode.Gone, "reset-used");
    }
}
