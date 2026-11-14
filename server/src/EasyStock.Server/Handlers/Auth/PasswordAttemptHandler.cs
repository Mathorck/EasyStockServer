using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>Outcome of <see cref="PasswordAttemptHandler.CheckAsync"/>.</summary>
public enum PasswordAttempt
{
    Right,
    Wrong,
    /// <summary>This attempt was wrong and locked the account.</summary>
    WrongAndLocked,
    /// <summary>The account was already locked; the password was not judged.</summary>
    Locked,
}

/// <summary>
/// Checks a password against the shared failed-attempt limit (5 failures, 15 minutes) of login and
/// <c>POST /me/password</c>. Identity's lockout columns are used but counted here with the injected clock, because
/// Identity's own lockout reads the system clock. The user row is locked (<c>FOR UPDATE</c>) from the lockout check
/// to the count update, so parallel guesses are judged one at a time and cannot get past the limit.
/// </summary>
public sealed class PasswordAttemptHandler(UserManager<AppUser> users, EasyStockDbContext db, TimeProvider time)
{
    private static string? _dummyHash;

    /// <summary>When a locked account's lockout ends, as last read by <see cref="CheckAsync"/>.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>Runs in its own transaction, so the caller must not have one open.</summary>
    public async Task<PasswordAttempt> CheckAsync(AppUser user, string password, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockoutEnd = await db.Database.SqlQuery<DateTimeOffset?>(
                $"""SELECT lockout_end AS "Value" FROM users WHERE id = {user.Id} FOR UPDATE""")
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        if (user.LockoutEnabled && lockoutEnd.FirstOrDefault() is { } end && end > now)
        {
            // Same work as a real check, so the timing does not tell that the account is locked.
            VerifyDummy(password);
            LockedUntil = end;
            return PasswordAttempt.Locked;
        }

        if (await users.CheckPasswordAsync(user, password))
        {
            await db.Users
                .Where(u => u.Id == user.Id && (u.AccessFailedCount != 0 || u.LockoutEnd != null))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AccessFailedCount, 0)
                    .SetProperty(x => x.LockoutEnd, (DateTimeOffset?)null), ct);
            await tx.CommitAsync(ct);
            return PasswordAttempt.Right;
        }

        var lockedUntil = user.LockoutEnabled ? await RecordFailureAsync(user.Id, now, ct) : null;
        await tx.CommitAsync(ct);
        LockedUntil = lockedUntil;
        return lockedUntil is null ? PasswordAttempt.Wrong : PasswordAttempt.WrongAndLocked;
    }

    /// <summary>Hashing work for a user that cannot be checked, so the timing matches a real check.</summary>
    public void VerifyDummy(string password)
    {
        _dummyHash ??= users.PasswordHasher.HashPassword(new AppUser(), "not-a-password-at-all");
        users.PasswordHasher.VerifyHashedPassword(new AppUser(), _dummyHash, password);
    }

    /// <summary>Counts a failure in one statement; returns the lockout end when this failure locks the account.</summary>
    private async Task<DateTimeOffset?> RecordFailureAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var until = now + AuthLimitsHelper.LockoutDuration;
        var max = AuthLimitsHelper.MaxFailedLogins;
        var rows = await db.Database.SqlQuery<DateTimeOffset?>($"""
            UPDATE users SET
                access_failed_count = CASE WHEN access_failed_count + 1 >= {max} THEN 0 ELSE access_failed_count + 1 END,
                lockout_end = CASE WHEN access_failed_count + 1 >= {max} THEN {until} ELSE lockout_end END
            WHERE id = {userId}
            RETURNING CASE WHEN access_failed_count = 0 THEN lockout_end END AS "Value"
            """).ToListAsync(ct);
        return rows.FirstOrDefault();
    }
}
