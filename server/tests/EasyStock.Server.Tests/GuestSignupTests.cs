using System.Net;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>Each test has its own database: the public QR code and the Guest count are server-wide.</summary>
public sealed class GuestSignupTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ApiFactory> NewServer()
    {
        var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        factory.CreateVersionedClient();
        return factory;
    }

    [Fact]
    public async Task Signup_is_off_until_an_admin_turns_it_on()
    {
        using var factory = await NewServer();
        await (await factory.GuestSignupAsync(SecretTokensHelper.New())).Problem(HttpStatusCode.Forbidden, "guest-signup-off");

        var token = await factory.EnableGuestSignupAsync();
        Assert.Equal(HttpStatusCode.OK, (await factory.GuestSignupAsync(token)).StatusCode);

        await factory.WithService<PublicInviteAnswer, PublicInviteHandler>(s => s.UpdateAsync(false, null, Ct));
        await (await factory.GuestSignupAsync(token)).Problem(HttpStatusCode.Forbidden, "guest-signup-off");
    }

    [Fact]
    public async Task Wrong_or_replaced_token_is_invalid()
    {
        using var factory = await NewServer();
        var old = await factory.EnableGuestSignupAsync();
        var current = await factory.WithService<PublicInviteAnswer, PublicInviteHandler>(s => s.RegenerateAsync(Ct));
        Assert.StartsWith("easystock://guest?server=", current.Qr);

        await (await factory.GuestSignupAsync(old)).Problem(HttpStatusCode.NotFound, "invite-invalid");
        await (await factory.GuestSignupAsync(SecretTokensHelper.New())).Problem(HttpStatusCode.NotFound, "invite-invalid");
        Assert.Equal(HttpStatusCode.OK, (await factory.GuestSignupAsync(current.Token!)).StatusCode);
    }

    [Fact]
    public async Task Public_token_only_gives_a_guest()
    {
        using var factory = await NewServer();
        var token = await factory.EnableGuestSignupAsync();
        var guest = await (await factory.GuestSignupAsync(token)).Read<TokenAnswer>();
        Assert.Equal("guest", guest.User.Role);
        Assert.Equal("guest", guest.User.Kind);

        var me = await (await factory.GetMeAsync(guest.AccessToken)).Read<MeAnswer>();
        Assert.Equal("guest", me.Role);
        Assert.Null(me.PendingPromotion);

        var client = factory.CreateVersionedClient(guest.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/test-probe/view-stock", Ct)).StatusCode);
        await (await client.GetAsync("/api/v1/test-probe/administer", Ct)).Problem(HttpStatusCode.Forbidden, "not-allowed");

        // The public token is not an invite.
        await (await factory.CreateVersionedClient().PostJson("/api/v1/auth/invite/redeem",
                new { inviteToken = token, username = "sneaky", displayName = "x", password = AuthHelpers.Password, deviceName = "x" }))
            .Problem(HttpStatusCode.NotFound, "invite-invalid");
    }

    [Fact]
    public async Task Guest_limit_is_enforced_and_logout_frees_a_place()
    {
        using var factory = await NewServer();
        var token = await factory.EnableGuestSignupAsync(guestLimit: 2);
        var first = await (await factory.GuestSignupAsync(token)).Read<TokenAnswer>();
        Assert.Equal(HttpStatusCode.OK, (await factory.GuestSignupAsync(token)).StatusCode);
        await (await factory.GuestSignupAsync(token)).Problem(HttpStatusCode.Conflict, "guest-limit-reached");

        var info = await factory.WithService<PublicInviteAnswer, PublicInviteHandler>(s => s.GetAsync(Ct));
        Assert.Equal(2, info.GuestCount);

        var logout = await factory.CreateVersionedClient(first.AccessToken).PostAsync("/api/v1/auth/logout", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.GuestSignupAsync(token)).StatusCode);
    }

    [Fact]
    public async Task One_address_gets_5_guest_signups_per_hour()
    {
        using var factory = await NewServer();
        var token = await factory.EnableGuestSignupAsync();
        var ip = ApiFactory.RandomIp();
        for (var i = 0; i < AuthLimitsHelper.GuestSignupsPerIpPerHour; i++)
            Assert.Equal(HttpStatusCode.OK, (await factory.GuestSignupAsync(token, ip)).StatusCode);

        var limited = await factory.GuestSignupAsync(token, ip);
        await limited.Problem(HttpStatusCode.TooManyRequests, "rate-limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await factory.GuestSignupAsync(token)).StatusCode);

        factory.Time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await factory.GuestSignupAsync(token, ip)).StatusCode);
    }
}
