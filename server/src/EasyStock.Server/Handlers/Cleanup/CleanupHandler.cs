using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Images;
using EasyStock.Server.Helpers;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Cleanup;

/// <summary>Rows touched by one cleanup run. Counts only: nothing identifying is ever logged.</summary>
public sealed record CleanupCounts(
    int PromotionsExpired, int Invites, int PasswordResets, int Promotions, int Sessions,
    int Guests, int SyncBatches, int Images);

/// <summary>
/// One cleanup pass (docs/03-server-spec.md, "Background jobs"). Sync tombstones (soft-deleted shared rows) and
/// stock movements are deliberately never purged: neither the spec nor the plan defines a retention that is safe for
/// a phone that stays offline for a long time, and movements are append-only.
/// </summary>
public sealed class CleanupHandler(EasyStockDbContext db, ImageHandler images, TimeProvider time)
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

        // Guests not seen for 90 days: soft-deleted like on logout (the row stays for the movements and the phones),
        // and their sessions are deleted below.
        var guestCutoff = now - CleanupHelper.GuestInactivity;
        var guests = await db.Users
            .Where(u => u.Kind == UserKind.Guest && u.DeletedAt == null && (u.LastSeenAt ?? u.CreatedAt) < guestCutoff)
            .ToListAsync(ct);
        foreach (var guest in guests) await AccountsHelper.SoftDeleteGuestAsync(db, guest, now, ct);
        var guestIds = guests.Select(g => g.Id).ToList();
        db.ChangeTracker.Clear();

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
            .Where(s => s.LastUsedAt < idleCutoff || (s.RevokedAt != null && s.RevokedAt < revokedCutoff)
                || guestIds.Contains(s.UserId))
            .ExecuteDeleteAsync(ct);

        // Saved push answers are only needed for a retry of the same batch.
        var batchCutoff = now - CleanupHelper.SyncBatchRetention;
        var batches = await db.Set<SyncBatch>().Where(b => b.ReceivedAt < batchCutoff).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);

        // Photos no article references, in a transaction of their own (under the sync and image locks), files last.
        var orphans = await images.DeleteOrphansAsync(db, ImagesHelper.OrphanGrace, time, ct);

        return new CleanupCounts(promotionsExpired, invites, resets, promotions, sessions, guests.Count, batches, orphans);
    }
}
