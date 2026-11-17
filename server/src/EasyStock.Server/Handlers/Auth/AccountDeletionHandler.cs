using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// Deletes a full or Guest account and erases its personal data (<see cref="AccountsHelper.EraseAccountAsync"/>),
/// shared by the Admin delete (<c>DELETE /admin/users/{id}</c>) and the self-service delete (<c>DELETE /me</c>).
/// Runs under the Admin-change advisory lock, so two deletes cannot remove the last two Admins at once.
/// </summary>
public sealed class AccountDeletionHandler(
    EasyStockDbContext db,
    SessionHandler sessions,
    PasswordAttemptLimiter limiter,
    TimeProvider time)
{
    /// <summary>404 not-found (no such live user), 409 last-admin.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AuthLimitsHelper.AdminChangeLockKey, ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.DeletedAt == null, ct)
            ?? throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypesHelper.NotFound);
        if (AccountsHelper.IsActiveAdmin(user)) await AccountsHelper.EnsureAnotherAdminAsync(db, id, ct);

        await AccountsHelper.EraseAccountAsync(db, user, now, ct);
        await sessions.RevokeAllAsync(id, AuthLimitsHelper.RevokeReasons.Deleted, keep: null, ct);
        await tx.CommitAsync(ct);
        // The in-memory failed-password counters are keyed by user and client address.
        limiter.Clear(id);
    }
}
