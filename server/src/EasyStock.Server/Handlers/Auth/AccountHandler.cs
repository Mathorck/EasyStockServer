using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
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
    /// 400 invalid-credentials. The other sessions end, the current one stays.
    /// </summary>
    public async Task ChangePasswordAsync(Guid userId, Guid sessionId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw AccountsHelper.Unauthenticated(ProblemTypesHelper.SessionRevoked);
        if (user.Kind != UserKind.Full)
            throw ProblemException.Of(StatusCodes.Status403Forbidden, ProblemTypesHelper.NotAllowed);

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
    /// <c>POST /auth/logout</c>: ends the session. A Guest account is deleted with everything that cascades from it
    /// (sessions, promotions).
    /// </summary>
    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var kind = await db.Users.Where(u => u.Id == userId).Select(u => (UserKind?)u.Kind).SingleOrDefaultAsync(ct);
        if (kind == UserKind.Guest)
        {
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
            LogGuestDeleted(log, userId);
            return;
        }
        await sessions.RevokeAsync(sessionId, AuthLimitsHelper.RevokeReasons.Logout, ct);
        LogLoggedOut(log, userId, sessionId);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged out of session {SessionId}")]
    private static partial void LogLoggedOut(ILogger log, Guid userId, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Guest {UserId} logged out; account deleted")]
    private static partial void LogGuestDeleted(ILogger log, Guid userId);
}
