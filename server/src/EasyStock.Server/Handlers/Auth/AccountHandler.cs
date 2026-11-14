using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Admin;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>The caller's own account: <c>/me</c> and logout.</summary>
public sealed partial class AccountHandler(
    EasyStockDbContext db,
    UserManager<AppUser> users,
    SessionHandler sessions,
    PromotionHandler promotions,
    PasswordAttemptHandler attempts,
    TimeProvider time,
    ILogger<AccountHandler> log)
{
    public async Task<MeAnswer> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        return await ToAnswerAsync(user, ct);
    }

    /// <summary>
    /// <c>PATCH /me</c>. A Guest may only change the display name (403 not-allowed for alertsEnabled). A new display
    /// name bumps <c>users.server_seq</c> through the trigger, so other phones pull it.
    /// </summary>
    public async Task<MeAnswer> UpdateAsync(Guid userId, UpdateMeRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (request.DisplayName is not null)
            user.DisplayName = AccountsHelper.CleanDisplayName(request.DisplayName);
        if (request.HasAlertsEnabled)
        {
            if (user.Kind == UserKind.Guest)
                throw ProblemException.Of(StatusCodes.Status403Forbidden, ProblemTypesHelper.NotAllowed);
            user.AlertsEnabled = request.AlertsEnabled;
        }
        await db.SaveChangesAsync(ct);
        return await ToAnswerAsync(user, ct);
    }

    /// <summary>
    /// <c>POST /me/password</c>: full accounts only (403 not-allowed for a Guest). A wrong current password is
    /// 400 invalid-credentials. Wrong current passwords count toward the same lockout as login; while the account is
    /// locked the answer is 429 rate-limited with retryAfter (the caller is signed in, so nothing leaks). The other
    /// sessions end, the current one stays.
    /// </summary>
    public async Task ChangePasswordAsync(Guid userId, Guid sessionId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw AccountsHelper.Unauthenticated(ProblemTypesHelper.SessionRevoked);
        if (user.Kind != UserKind.Full)
            throw ProblemException.Of(StatusCodes.Status403Forbidden, ProblemTypesHelper.NotAllowed);

        switch (await attempts.CheckAsync(user, request.CurrentPassword, ct))
        {
            case PasswordAttempt.Right:
                break;
            case PasswordAttempt.Locked:
                throw new ProblemException(StatusCodes.Status429TooManyRequests, ProblemTypesHelper.RateLimited,
                    retryAfter: attempts.LockedUntil!.Value - time.GetUtcNow());
            case PasswordAttempt.WrongAndLocked:
                LogLocked(log, userId);
                throw ProblemException.Of(StatusCodes.Status400BadRequest, ProblemTypesHelper.InvalidCredentials);
            default:
                throw ProblemException.Of(StatusCodes.Status400BadRequest, ProblemTypesHelper.InvalidCredentials);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            throw ProblemException.Of(StatusCodes.Status400BadRequest, ProblemTypesHelper.InvalidCredentials);
        AccountsHelper.ThrowIfFailed(result);
        await sessions.RevokeAllAsync(userId, AuthLimitsHelper.RevokeReasons.PasswordChanged, keep: sessionId, ct);
        await tx.CommitAsync(ct);
        LogPasswordChanged(log, userId);
    }

    /// <summary>
    /// <c>POST /auth/logout</c>: ends the session. A Guest account is soft-deleted (name removed, sessions revoked,
    /// pending promotion cancelled); the row stays for its movements.
    /// </summary>
    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var kind = await db.Users.Where(u => u.Id == userId).Select(u => (UserKind?)u.Kind).SingleOrDefaultAsync(ct);
        if (kind == UserKind.Guest)
        {
            await DeleteGuestAsync(userId, ct);
            LogGuestDeleted(log, userId);
            return;
        }
        await sessions.RevokeAsync(sessionId, AuthLimitsHelper.RevokeReasons.Logout, ct);
        LogLoggedOut(log, userId, sessionId);
    }

    /// <summary>
    /// Soft delete, like an Admin deleting a full account: a hard delete would leave no <c>users.server_seq</c> bump,
    /// so other phones would keep the Guest's name for ever. The trigger bumps the seq when <c>deleted_at</c>
    /// changes, and phones pull "Deleted user". Sessions are revoked; the Guest no longer counts against the limit.
    /// </summary>
    private async Task DeleteGuestAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        await AccountsHelper.SoftDeleteGuestAsync(db, user, now, ct);
        await sessions.RevokeAllAsync(userId, AuthLimitsHelper.RevokeReasons.Deleted, keep: null, ct);
        await tx.CommitAsync(ct);
    }

    private async Task<MeAnswer> ToAnswerAsync(AppUser u, CancellationToken ct) => new(
        u.Id,
        u.DisplayName,
        u.Role.ToWire(),
        u.Kind.KindToWire(),
        ShortCodeHelper.Format(u.ShortCode),
        u.AlertsEnabled,
        u.Kind == UserKind.Guest ? await promotions.GetPendingAsync(u.Id, ct) : null);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} changed their password; other sessions revoked")]
    private static partial void LogPasswordChanged(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} locked out after too many wrong current passwords")]
    private static partial void LogLocked(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged out of session {SessionId}")]
    private static partial void LogLoggedOut(ILogger log, Guid userId, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Guest {UserId} logged out; account deleted")]
    private static partial void LogGuestDeleted(ILogger log, Guid userId);
}
