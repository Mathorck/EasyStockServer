using System.Net;
using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class InviteTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> Redeem(string? token, string? username = null, string password = AuthHelpers.Password) =>
        _factory.CreateVersionedClient().PostJson("/api/v1/auth/invite/redeem",
            new { inviteToken = token, username = username ?? AuthHelpers.UniqueName(), displayName = "New", password, deviceName = "Pixel" });

    [Fact]
    public async Task Invite_creates_a_full_account_with_its_role()
    {
        var invite = await _factory.CreateInviteAsync(Role.Viewer);
        Assert.StartsWith("easystock://invite?server=https%3A%2F%2Fstock.example.org&token=", invite.Qr);
        Assert.Equal(invite.CreatedAt + AuthLimitsHelper.InviteLifetime, invite.ExpiresAt);

        var answer = await (await Redeem(invite.Token)).Read<TokenAnswer>();
        Assert.Equal("viewer", answer.User.Role);
        Assert.Equal("full", answer.User.Kind);
    }

    [Fact]
    public async Task Invite_is_single_use()
    {
        var invite = await _factory.CreateInviteAsync(Role.Member);
        Assert.Equal(HttpStatusCode.OK, (await Redeem(invite.Token)).StatusCode);
        await (await Redeem(invite.Token)).Problem(HttpStatusCode.Gone, "invite-used");
    }

    [Fact]
    public async Task Invite_expires_after_7_days()
    {
        var invite = await _factory.CreateInviteAsync(Role.Member);
        _factory.Time.Advance(AuthLimitsHelper.InviteLifetime + TimeSpan.FromMinutes(1));
        await (await Redeem(invite.Token)).Problem(HttpStatusCode.Gone, "invite-expired");
    }

    [Fact]
    public async Task Unknown_or_cancelled_invite_is_invalid()
    {
        await (await Redeem(SecretTokensHelper.New())).Problem(HttpStatusCode.NotFound, "invite-invalid");

        var invite = await _factory.CreateInviteAsync(Role.Member);
        var open = await _factory.WithService<IReadOnlyList<InviteAnswer>, InviteHandler>(s => s.ListOpenAsync(Ct));
        Assert.Contains(open, i => i.Id == invite.Id && i.Token == null);
        await _factory.WithService<bool, InviteHandler>(async s => { await s.CancelAsync(invite.Id, Ct); return true; });
        await (await Redeem(invite.Token)).Problem(HttpStatusCode.NotFound, "invite-invalid");

        var missing = await Assert.ThrowsAsync<ProblemException>(() =>
            _factory.WithService<bool, InviteHandler>(async s => { await s.CancelAsync(invite.Id, Ct); return true; }));
        Assert.Equal(ProblemTypesHelper.NotFound, missing.Type);
    }

    [Fact]
    public async Task Failed_redeem_keeps_the_invite()
    {
        var existing = await _factory.CreateUserAsync();
        var invite = await _factory.CreateInviteAsync(Role.Member);

        await (await Redeem(invite.Token, existing.Username.ToUpperInvariant())).Problem(HttpStatusCode.Conflict, "username-taken");
        var problem = await (await Redeem(invite.Token, password: "short")).Problem(HttpStatusCode.BadRequest, "validation-failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("password", out _));
        await (await Redeem(invite.Token, password: new string('p', 129))).Problem(HttpStatusCode.BadRequest, "validation-failed");
        await (await Redeem(invite.Token, username: "no spaces")).Problem(HttpStatusCode.BadRequest, "validation-failed");

        Assert.Equal(HttpStatusCode.OK, (await Redeem(invite.Token)).StatusCode);
    }

    [Fact]
    public async Task Guest_invites_do_not_exist()
    {
        var ex = await Assert.ThrowsAsync<ProblemException>(() => _factory.CreateInviteAsync(Role.Guest));
        Assert.Equal(ProblemTypesHelper.RoleNotAllowed, ex.Type);
    }
}
