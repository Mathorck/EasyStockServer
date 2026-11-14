using System.Net;
using System.Text.Json;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using EasyStock.Server.Tests.Infrastructure;
using Npgsql;
using Xunit;

namespace EasyStock.Server.Tests;

/// <summary>Each test has its own database (Guests come from the server-wide public QR code).</summary>
public sealed class PromotionTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, TokenAnswer Guest)> NewGuest(string? connectionString = null)
    {
        var factory = new ApiFactory(connectionString ?? await pg.CreateDatabaseAsync());
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

    [Fact]
    public async Task A_promotion_cancelled_while_completing_is_not_completed()
    {
        var connectionString = await pg.CreateDatabaseAsync();
        var (factory, guest) = await NewGuest(connectionString);
        using var _ = factory;
        await Promote(factory, guest.User.Id, Role.Member, guest.User.ShortCode);

        // An Admin's cancel, not committed yet, when the Guest completes.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(Ct);
        await using var cancel = await conn.BeginTransactionAsync(Ct);
        await using (var update = new NpgsqlCommand(
            "UPDATE promotion SET ended_at = now(), end_reason = 'cancelled' WHERE user_id = @id AND ended_at IS NULL", conn, cancel))
        {
            update.Parameters.AddWithValue("id", guest.User.Id);
            Assert.Equal(1, await update.ExecuteNonQueryAsync(Ct));
        }

        var completing = Complete(factory, guest.AccessToken, "racer");
        await WaitForLockWaitAsync(connectionString, completing);
        await cancel.CommitAsync(Ct);

        await (await completing).Problem(HttpStatusCode.NotFound, "no-promotion");
        var me = await (await factory.GetMeAsync(guest.AccessToken)).Read<MeAnswer>();
        Assert.Equal("guest", me.Kind);
    }

    /// <summary>
    /// Until a session of the database waits on a lock (the completing request). Polled from a connection of its own,
    /// outside any transaction: pg_stat_activity is a snapshot taken once per transaction.
    /// </summary>
    private static async Task WaitForLockWaitAsync(string connectionString, Task request)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(Ct);
        for (var i = 0; i < 400 && !request.IsCompleted; i++)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND wait_event_type = 'Lock'",
                conn);
            if ((long)(await cmd.ExecuteScalarAsync(Ct))! > 0) return;
            await Task.Delay(25, Ct);
        }
        Assert.Fail("the completing request never waited on the cancel's lock");
    }

    [Fact]
    public async Task Promoted_guest_pulls_everything_again_without_a_new_dataset_epoch()
    {
        var (factory, guest) = await NewGuest();
        using var _ = factory;
        await Promote(factory, guest.User.Id, Role.Member, guest.User.ShortCode);
        var location = new Location { Id = Guid.NewGuid(), Name = "loc", CreatedAt = 1, UpdatedAt = 1 };
        var review = new StockReview { Id = Guid.NewGuid(), LocationId = location.Id, StartedAt = 1, CreatedAt = 1, UpdatedAt = 1 };
        await factory.WithDb(async db =>
        {
            db.AddRange(location, review);
            await db.SaveChangesAsync(Ct);
        });
        var epoch = await ServerEpoch(factory);
        var member = await factory.CreateUserAsync(Role.Member);
        var memberClient = factory.CreateVersionedClient(member.AccessToken);

        // As a Guest: no review, and a cursor past it.
        var guestPage = await PullPage(factory.CreateVersionedClient(guest.AccessToken), 0, null);
        Assert.False(HasReview(guestPage, review.Id));
        var guestCursor = guestPage.GetProperty("next").GetInt64();
        Assert.True(guestCursor > 0);
        var memberCursor = (await PullPage(memberClient, 0, null)).GetProperty("next").GetInt64();

        var promoted = await (await Complete(factory, guest.AccessToken, "promoted")).Read<TokenAnswer>();
        var client = factory.CreateVersionedClient(promoted.AccessToken);

        // The incremental pull is refused like after a restore, with the same epoch: the phone pulls from 0.
        var problem = await (await Pull(client, guestCursor, epoch)).Problem(HttpStatusCode.Conflict, "dataset-reset");
        Assert.Equal(epoch, problem.GetProperty("datasetEpoch").GetString());
        Assert.Equal(epoch, await ServerEpoch(factory));

        var full = await PullPage(client, 0, null);
        Assert.True(HasReview(full, review.Id));
        Assert.Equal(epoch, full.GetProperty("datasetEpoch").GetString());
        // Done once: the next incremental pulls are plain.
        await PullPage(client, guestCursor, epoch);

        // Nobody else is affected.
        await PullPage(memberClient, memberCursor, epoch);
    }

    private static Task<HttpResponseMessage> Pull(HttpClient client, long since, string? epoch)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/sync/pull?since={since}");
        if (epoch is not null) request.Headers.Add("X-EasyStock-Dataset-Epoch", epoch);
        return client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> PullPage(HttpClient client, long since, string? epoch) =>
        await (await Pull(client, since, epoch)).Read<JsonElement>();

    private static bool HasReview(JsonElement page, Guid id) =>
        page.GetProperty("rows").TryGetProperty("stock_review", out var reviews)
        && reviews.EnumerateArray().Any(r => r.GetProperty("id").GetString() == id.ToString());

    private static async Task<string> ServerEpoch(ApiFactory factory) =>
        (await (await factory.CreateVersionedClient().GetAsync("/api/v1/server-info", Ct)).Read<JsonElement>())
            .GetProperty("datasetEpoch").GetString()!;
}
