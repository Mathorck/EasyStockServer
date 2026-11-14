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
/// Admin-made password reset codes (24 hours, single use). Redeeming one sets the new password, clears the
/// lockout, changes the security stamp, ends every session of the user and opens a new one.
/// </summary>
public sealed partial class PasswordResetHandler(
    EasyStockDbContext db,
    UserManager<AppUser> users,
    SessionHandler sessions,
    QrContentHandler qr,
    PasswordAttemptLimiter attempts,
    TimeProvider time,
    ILogger<PasswordResetHandler> log)
{
    /// <summary>
    /// A new reset code for a full account (Admin endpoint). Older unused codes of the user stop working.
    /// 404 not-found for an unknown or deleted user, 409 use-promotion for a Guest (Guests have no password).
    /// </summary>
    public async Task<QrGrant> CreateAsync(Guid userId, Guid? createdBy, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct)
            ?? throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypesHelper.NotFound);
        if (user.Kind == UserKind.Guest)
            throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypesHelper.UsePromotion);

        var now = time.GetUtcNow();
        var token = SecretTokensHelper.New();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.PasswordResets.Where(r => r.UserId == userId && r.UsedAt == null).ExecuteDeleteAsync(ct);
        var reset = new PasswordReset
        {
            Id = Guid.CreateVersion7(now),
            TokenHash = SecretTokensHelper.Hash(token),
            UserId = userId,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = now + AuthLimitsHelper.ResetLifetime,
        };
        db.PasswordResets.Add(reset);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        LogCreated(log, userId);
        return new QrGrant(token, qr.Build(QrContentHandler.Reset, token), reset.ExpiresAt);
    }

    /// <summary><c>POST /auth/reset</c>.</summary>
    public async Task<TokenAnswer> RedeemAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = SecretTokensHelper.Hash(request.ResetToken);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reset = await db.PasswordResets.AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == hash, ct)
            ?? throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypesHelper.InviteInvalid);
        if (reset.UsedAt is not null) throw Gone(ProblemTypesHelper.ResetUsed);
        if (reset.ExpiresAt <= now) throw Gone(ProblemTypesHelper.ResetExpired);
        var claimed = await db.PasswordResets.Where(r => r.Id == reset.Id && r.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), ct);
        if (claimed == 0) throw Gone(ProblemTypesHelper.ResetUsed);

        var user = await users.FindByIdAsync(reset.UserId.ToString());
        if (user is null || user.Kind != UserKind.Full)
            throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypesHelper.InviteInvalid);
        if (user.DisabledAt is not null || user.DeletedAt is not null)
            throw AccountsHelper.Unauthenticated(ProblemTypesHelper.AccountDisabled);

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        AccountsHelper.ThrowIfFailed(await users.RemovePasswordAsync(user));
        AccountsHelper.ThrowIfFailed(await users.AddPasswordAsync(user, request.Password));
        await sessions.RevokeAllAsync(user.Id, AuthLimitsHelper.RevokeReasons.PasswordReset, keep: null, ct);
        var answer = await sessions.CreateAsync(user, request.DeviceName, ct);
        await tx.CommitAsync(ct);
        attempts.Clear(user.Id);
        LogRedeemed(log, user.Id);
        return answer;
    }

    private static ProblemException Gone(string type) => ProblemException.Of(StatusCodes.Status410Gone, type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset code created for user {UserId}")]
    private static partial void LogCreated(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password of user {UserId} reset; other sessions revoked")]
    private static partial void LogRedeemed(ILogger log, Guid userId);
}
