using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Helpers;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Cleanup;

/// <summary>Rows touched by one cleanup run. Counts only: nothing identifying is ever logged.</summary>
public sealed record CleanupCounts(int PromotionsExpired, int Invites, int PasswordResets, int Promotions, int Sessions);

/// <summary>
/// One cleanup pass. Sync tombstones (soft-deleted shared rows) are deliberately not purged: neither the spec nor
/// the plan defines a retention that is safe for a phone that stays offline for a long time.
/// </summary>
public sealed class CleanupHandler(EasyStockDbContext db, TimeProvider time)
{
    /// <summary>Null when another instance holds the cleanup lock (the run is skipped).</summary>
    public async Task<CleanupCounts?> RunOnceAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({CleanupHelper.LockKey}) AS \"Value\"")
            .SingleAsync(ct);
        if (!locked) return null;

        var graceCutoff = now - CleanupHelper.ExpiredGrace;
        var promotionsExpired = await db.Promotions
            .Where(p => p.EndedAt == null && p.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.EndedAt, p => p.ExpiresAt)
                .SetProperty(p => p.EndReason, Promotion.Expired), ct);
        var invites = await db.Invites.Where(i => i.ExpiresAt < graceCutoff).ExecuteDeleteAsync(ct);
        var resets = await db.PasswordResets.Where(r => r.ExpiresAt < graceCutoff).ExecuteDeleteAsync(ct);
        var promotions = await db.Promotions.Where(p => p.ExpiresAt < graceCutoff && p.EndedAt != null).ExecuteDeleteAsync(ct);

        // Idle for the refresh lifetime (expired) or revoked for the grace period. Push registrations cascade.
        var idleCutoff = now - AuthLimitsHelper.RefreshIdleLifetime;
        var revokedCutoff = now - CleanupHelper.RevokedSessionGrace;
        var sessions = await db.DeviceSessions
            .Where(s => s.LastUsedAt < idleCutoff || (s.RevokedAt != null && s.RevokedAt < revokedCutoff))
            .ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
        return new CleanupCounts(promotionsExpired, invites, resets, promotions, sessions);
    }
}
