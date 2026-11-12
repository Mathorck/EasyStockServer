using System.Net;
using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Tests.Infrastructure;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>Each test has its own database (Guests come from the server-wide public QR code).</summary>
public sealed class PromotionTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, TokenAnswer Guest)> NewGuest()
    {
        var factory = new ApiFactory(await pg.CreateDatabaseAsync());
        var token = await factory.EnableGuestSignupAsync();
        var guest = await (await factory.GuestSignupAsync(token)).Read<TokenAnswer>();
        return (factory, guest);
    }

    private static Task<PendingPromotionAnswer> Promote(ApiFactory factory, Guid userId, Role role, string code) =>
        factory.WithService<PendingPromotionAnswer, PromotionHandler>(s => s.CreateAsync(userId, role, code, null, Ct));

    private static Task<HttpResponseMessage> Complete(ApiFactory factory, string accessToken, string username) =>
        factory.CreateVersionedClient(accessToken).PostJson("/api/v1/auth/complete-promotion", new { username, password = AuthHelpers.Password });

    [Fact]
    public async Task Promoted_guest_becomes_a_full_account_on_the_same_session()
    {
        var (factory, guest) = await NewGuest();
        using var _ = factory;

        var mismatch = await Assert.ThrowsAsync<ProblemException>(() => Promote(factory, guest.User.Id, Role.Member, "AAA-AAA"));
        Assert.Equal(ProblemTypesHelper.ShortCodeMismatch, mismatch.Type);

        // Typed by an Admin: case and dash do not matter.
        await Promote(factory, guest.User.Id, Role.Member, guest.User.ShortCode.Replace("-", "").ToLowerInvariant());
        var me = await (await factory.GetMeAsync(guest.AccessToken)).Read<MeAnswer>();
        Assert.Equal("member", me.PendingPromotion?.Role);

        var answer = await (await Complete(factory, guest.AccessToken, "promoted")).Read<TokenAnswer>();
        Assert.Equal(guest.User.Id, answer.User.Id);
        Assert.Equal("member", answer.User.Role);
        Assert.Equal("full", answer.User.Kind);

        me = await (await factory.GetMeAsync(answer.AccessToken)).Read<MeAnswer>();
        Assert.Equal("full", me.Kind);
        Assert.Null(me.PendingPromotion);
        Assert.Equal(HttpStatusCode.OK, (await factory.RefreshAsync(answer.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.LoginAsync("promoted", AuthHelpers.Password)).StatusCode);

        await (await Complete(factory, answer.AccessToken, "again")).Problem(HttpStatusCode.NotFound, "no-promotion");
    }

    [Fact]
    public async Task Promotion_expires_after_7_days()
    {
        var (factory, guest) = await NewGuest();
        using var _ = factory;
        await Promote(factory, guest.User.Id, Role.Viewer, guest.User.ShortCode);

        factory.Time.Advance(AuthLimitsHelper.PromotionLifetime + TimeSpan.FromMinutes(1));
        var refreshed = await (await factory.RefreshAsync(guest.RefreshToken)).Read<TokenAnswer>();
        Assert.Null((await (await factory.GetMeAsync(refreshed.AccessToken)).Read<MeAnswer>()).PendingPromotion);
        await (await Complete(factory, refreshed.AccessToken, "late")).Problem(HttpStatusCode.Gone, "promotion-expired");
        await (await Complete(factory, refreshed.AccessToken, "late")).Problem(HttpStatusCode.NotFound, "no-promotion");
    }

    [Fact]
    public async Task Taken_username_keeps_the_promotion_pending()
    {
        var (factory, guest) = await NewGuest();
        using var _ = factory;
        var other = await factory.CreateUserAsync();
        await Promote(factory, guest.User.Id, Role.Member, guest.User.ShortCode);

        await (await Complete(factory, guest.AccessToken, other.Username)).Problem(HttpStatusCode.Conflict, "username-taken");
        await (await Complete(factory, guest.AccessToken, "a b")).Problem(HttpStatusCode.BadRequest, "validation-failed");
        Assert.Equal(HttpStatusCode.OK, (await Complete(factory, guest.AccessToken, "fresh-name")).StatusCode);
    }

    [Fact]
    public async Task Cancelled_or_missing_promotion()
    {
        var (factory, guest) = await NewGuest();
        using var _ = factory;
        await (await Complete(factory, guest.AccessToken, "nobody")).Problem(HttpStatusCode.NotFound, "no-promotion");

        await Promote(factory, guest.User.Id, Role.Member, guest.User.ShortCode);
        await factory.WithService<bool, PromotionHandler>(async s => { await s.CancelAsync(guest.User.Id, Ct); return true; });
        await (await Complete(factory, guest.AccessToken, "nobody")).Problem(HttpStatusCode.NotFound, "no-promotion");

        var ex = await Assert.ThrowsAsync<ProblemException>(() =>
            factory.WithService<bool, PromotionHandler>(async s => { await s.CancelAsync(guest.User.Id, Ct); return true; }));
        Assert.Equal(ProblemTypesHelper.NoPromotion, ex.Type);
    }

    [Fact]
    public async Task Only_guests_are_promoted_and_only_full_accounts_are_reset()
    {
        var (factory, guest) = await NewGuest();
        using var _ = factory;
        var full = await factory.CreateUserAsync(Role.Viewer);

        Assert.Equal(ProblemTypesHelper.RoleNotAllowed,
            (await Assert.ThrowsAsync<ProblemException>(() => Promote(factory, full.Id, Role.Member, full.Tokens.User.ShortCode))).Type);
        Assert.Equal(ProblemTypesHelper.RoleNotAllowed,
            (await Assert.ThrowsAsync<ProblemException>(() => Promote(factory, guest.User.Id, Role.Guest, guest.User.ShortCode))).Type);
        Assert.Equal(ProblemTypesHelper.UsePromotion,
            (await Assert.ThrowsAsync<ProblemException>(() =>
                factory.WithService<QrGrant, PasswordResetHandler>(s => s.CreateAsync(guest.User.Id, null, Ct)))).Type);
    }
}
