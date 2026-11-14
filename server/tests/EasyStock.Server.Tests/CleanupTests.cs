using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Cleanup;
using EasyStock.Server.Helpers;
using EasyStock.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EasyStock.Server.Tests;

public sealed class CleanupTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, cleanupJob: true);

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<CleanupCounts?> RunAsync() =>
        _factory.WithService<CleanupCounts?, CleanupHandler>(h => h.RunOnceAsync(Ct));

    private static byte[] Hash() => Guid.NewGuid().ToByteArray();

    [Fact]
    public async Task Invites_resets_and_promotions_are_deleted_only_after_the_grace_period()
    {
        var user = await _factory.CreateUserAsync();
        var other = await _factory.CreateUserAsync();
        var now = _factory.Time.GetUtcNow();
        var old = now - CleanupHelper.ExpiredGrace - TimeSpan.FromDays(1);
        var recent = now - TimeSpan.FromDays(1);
        Guid oldInvite = Guid.NewGuid(), recentInvite = Guid.NewGuid(), oldReset = Guid.NewGuid(), recentReset = Guid.NewGuid();
        Guid oldPromotion = Guid.NewGuid(), recentPromotion = Guid.NewGuid();
        await _factory.WithDb(async db =>
        {
            db.Invites.AddRange(
                new Invite { Id = oldInvite, TokenHash = Hash(), Role = Role.Member, CreatedAt = old, ExpiresAt = old },
                new Invite { Id = recentInvite, TokenHash = Hash(), Role = Role.Member, CreatedAt = recent, ExpiresAt = recent });
            db.PasswordResets.AddRange(
                new PasswordReset { Id = oldReset, TokenHash = Hash(), UserId = user.Id, CreatedAt = old, ExpiresAt = old },
                new PasswordReset { Id = recentReset, TokenHash = Hash(), UserId = user.Id, CreatedAt = recent, ExpiresAt = recent });
            db.Promotions.AddRange(
                new Promotion { Id = oldPromotion, UserId = user.Id, TargetRole = Role.Member, CreatedAt = old, ExpiresAt = old },
                new Promotion { Id = recentPromotion, UserId = other.Id, TargetRole = Role.Member, CreatedAt = recent, ExpiresAt = recent });
            await db.SaveChangesAsync(Ct);
        });

        var counts = await RunAsync();

        Assert.NotNull(counts);
        Assert.True(counts.Invites >= 1 && counts.PasswordResets >= 1 && counts.Promotions >= 1);
        await _factory.WithDb(async db =>
        {
            Assert.False(await db.Invites.AnyAsync(i => i.Id == oldInvite, Ct));
            Assert.True(await db.Invites.AnyAsync(i => i.Id == recentInvite, Ct));
            Assert.False(await db.PasswordResets.AnyAsync(r => r.Id == oldReset, Ct));
            Assert.True(await db.PasswordResets.AnyAsync(r => r.Id == recentReset, Ct));
            Assert.False(await db.Promotions.AnyAsync(p => p.Id == oldPromotion, Ct));
            // Expired a day ago: marked expired, kept until the grace period ends.
            var kept = await db.Promotions.SingleAsync(p => p.Id == recentPromotion, Ct);
            Assert.Equal(Promotion.Expired, kept.EndReason);
            Assert.True((recent - kept.EndedAt!.Value).Duration() < TimeSpan.FromMilliseconds(1));
        });
    }

    [Fact]
    public async Task A_pending_promotion_that_has_not_expired_is_left_alone()
    {
        var user = await _factory.CreateUserAsync();
        var id = Guid.NewGuid();
        await _factory.WithDb(async db =>
        {
            db.Promotions.Add(new Promotion
            {
                Id = id, UserId = user.Id, TargetRole = Role.Member,
                CreatedAt = _factory.Time.GetUtcNow(), ExpiresAt = _factory.Time.GetUtcNow().AddDays(1),
            });
            await db.SaveChangesAsync(Ct);
        });

        await RunAsync();

        await _factory.WithDb(async db =>
        {
            var p = await db.Promotions.SingleAsync(x => x.Id == id, Ct);
            Assert.Null(p.EndedAt);
            Assert.Null(p.EndReason);
        });
    }

    [Fact]
    public async Task Idle_and_long_revoked_sessions_are_deleted_with_their_push_registration()
    {
        var idle = await _factory.SignInAsync(Role.Member);
        var revokedLong = await _factory.SignInAsync(Role.Member);
        var revokedRecently = await _factory.SignInAsync(Role.Member);
        var active = await _factory.SignInAsync(Role.Member);
        var now = _factory.Time.GetUtcNow();
        await _factory.WithDb(async db =>
        {
            var sessions = await db.DeviceSessions
                .Where(s => new[] { idle.SessionId, revokedLong.SessionId, revokedRecently.SessionId, active.SessionId }.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, Ct);
            sessions[idle.SessionId].LastUsedAt = now - AuthLimitsHelper.RefreshIdleLifetime - TimeSpan.FromDays(1);
            sessions[revokedLong.SessionId].RevokedAt = now - CleanupHelper.RevokedSessionGrace - TimeSpan.FromDays(1);
            sessions[revokedLong.SessionId].RevokeReason = AuthLimitsHelper.RevokeReasons.Logout;
            sessions[revokedRecently.SessionId].RevokedAt = now - TimeSpan.FromDays(1);
            sessions[revokedRecently.SessionId].RevokeReason = AuthLimitsHelper.RevokeReasons.Admin;
            foreach (var id in sessions.Keys)
                db.Set<PushRegistration>().Add(new PushRegistration { DeviceSessionId = id, Endpoint = "https://ntfy.example.org/up", CreatedAt = 1, UpdatedAt = 1 });
            await db.SaveChangesAsync(Ct);
        });

        var counts = await RunAsync();

        Assert.NotNull(counts);
        Assert.True(counts.Sessions >= 2);
        await _factory.WithDb(async db =>
        {
            Assert.False(await db.DeviceSessions.AnyAsync(s => s.Id == idle.SessionId, Ct));
            Assert.False(await db.Set<PushRegistration>().AnyAsync(p => p.DeviceSessionId == idle.SessionId, Ct));
            Assert.False(await db.DeviceSessions.AnyAsync(s => s.Id == revokedLong.SessionId, Ct));
            Assert.False(await db.Set<PushRegistration>().AnyAsync(p => p.DeviceSessionId == revokedLong.SessionId, Ct));
            Assert.True(await db.DeviceSessions.AnyAsync(s => s.Id == revokedRecently.SessionId, Ct));
            Assert.True(await db.DeviceSessions.AnyAsync(s => s.Id == active.SessionId, Ct));
            Assert.True(await db.Set<PushRegistration>().AnyAsync(p => p.DeviceSessionId == active.SessionId, Ct));
        });
    }

    [Fact]
    public async Task A_run_is_skipped_while_another_instance_holds_the_lock()
    {
        await using var holder = new NpgsqlConnection(pg.ConnectionString);
        await holder.OpenAsync(Ct);
        await using var tx = await holder.BeginTransactionAsync(Ct);
        await using (var cmd = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({CleanupHelper.LockKey})", holder, tx))
            await cmd.ExecuteNonQueryAsync(Ct);

        Assert.Null(await RunAsync());

        await tx.CommitAsync(Ct);
        Assert.NotNull(await RunAsync());
    }

    [Fact]
    public async Task The_service_runs_after_the_first_delay_and_logs_counts_only()
    {
        var user = await _factory.CreateUserAsync();
        var old = _factory.Time.GetUtcNow() - CleanupHelper.ExpiredGrace - TimeSpan.FromDays(1);
        var secretHash = Hash();
        var inviteId = Guid.NewGuid();
        await _factory.WithDb(async db =>
        {
            db.Invites.Add(new Invite { Id = inviteId, TokenHash = secretHash, Role = Role.Member, CreatedAt = old, ExpiresAt = old });
            await db.SaveChangesAsync(Ct);
        });

        // The host (and so the job's timer) starts with the first client.
        _factory.CreateVersionedClient();
        for (var i = 0; i < 200 && !_factory.Logs.Events.Any(e => e.MessageTemplate.Text.StartsWith("Cleanup:")); i++)
        {
            _factory.Time.Advance(CleanupHelper.FirstRunDelay);
            await Task.Delay(50, Ct);
        }

        var log = Assert.Single(_factory.Logs.Events.Where(e => e.MessageTemplate.Text.StartsWith("Cleanup:")).Take(1));
        var text = LogCapture.Text(log);
        Assert.DoesNotContain(inviteId.ToString(), text);
        Assert.DoesNotContain(user.Username, text);
        await _factory.WithDb(async db => Assert.False(await db.Invites.AnyAsync(i => i.Id == inviteId, Ct)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("often")]
    public void An_invalid_interval_fails_startup(string value)
    {
        using var factory = new ApiFactory(pg.ConnectionString, settings: new Dictionary<string, string>
        {
            ["EASYSTOCK_CLEANUP_INTERVAL_MINUTES"] = value,
        });
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains("EASYSTOCK_CLEANUP_INTERVAL_MINUTES", string.Join(" | ", Chain(ex)));
    }

    private static IEnumerable<string> Chain(Exception? e)
    {
        for (; e is not null; e = e.InnerException) yield return e.Message;
    }
}
