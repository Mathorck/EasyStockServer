using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.DB;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// <c>POST /auth/login</c>. Lockout (5 failures, 15 minutes) uses Identity's columns but is counted here with
/// the injected clock, because Identity's own lockout reads the system clock. "Disabled" is only told after a
/// correct password, so the answer never reveals whether an account exists.
/// </summary>
public sealed partial class LoginHandler(
    UserManager<AppUser> users, EasyStockDbContext db, SessionHandler sessions, TimeProvider time, ILogger<LoginHandler> log)
{
    private static string? _dummyHash;

    public async Task<TokenAnswer> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByNameAsync(request.Username);
        if (user is null || user.Kind != UserKind.Full || user.PasswordHash is null)
        {
            // Same work as a real check, so the timing does not tell which usernames exist.
            _dummyHash ??= users.PasswordHasher.HashPassword(new AppUser(), "not-a-password-at-all");
            users.PasswordHasher.VerifyHashedPassword(new AppUser(), _dummyHash, request.Password);
            LogUnknown(log);
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.InvalidCredentials);
        }

        var now = time.GetUtcNow();
        if (user.LockoutEnabled && user.LockoutEnd > now)
            throw Locked(user.LockoutEnd.Value - now);

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            var lockedUntil = user.LockoutEnabled ? await RecordFailureAsync(user.Id, now, ct) : null;
            if (lockedUntil is { } until)
            {
                LogLocked(log, user.Id);
                throw Locked(until - now);
            }
            LogFailed(log, user.Id);
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.InvalidCredentials);
        }

        if (user.DisabledAt is not null || user.DeletedAt is not null)
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.AccountDisabled);

        if (user.AccessFailedCount != 0 || user.LockoutEnd is not null)
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AccessFailedCount, 0)
                .SetProperty(x => x.LockoutEnd, (DateTimeOffset?)null), ct);

        var answer = await sessions.CreateAsync(user, request.DeviceName, ct);
        LogLoggedIn(log, user.Id);
        return answer;
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

    private static ProblemException Locked(TimeSpan wait) =>
        new(StatusCodes.Status401Unauthorized, ProblemTypesHelper.AccountLocked, retryAfter: wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged in")]
    private static partial void LogLoggedIn(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Failed login for user {UserId}")]
    private static partial void LogFailed(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} locked out after too many failed logins")]
    private static partial void LogLocked(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Failed login for an unknown username")]
    private static partial void LogUnknown(ILogger log);
}
