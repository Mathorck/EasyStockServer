using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// Promotions of a Guest to a full account (7 days, one pending per user, a new one replaces it). The Guest keeps
/// Guest rights until they choose a username and password (complete-promotion). The Admin endpoints call
/// Create and Cancel; telling the Guest's phones (push) is up to the caller.
/// </summary>
public sealed partial class PromotionHandler(
    EasyStockDbContext db,
    UserManager<AppUser> users,
    SessionHandler sessions,
    TimeProvider time,
    ILogger<PromotionHandler> log)
{
    /// <summary>
    /// 404 not-found (unknown or deleted user), 409 role-not-allowed (not a Guest, or target role guest),
    /// 400 short-code-mismatch (the code the Admin typed is not the Guest's).
    /// </summary>
    public async Task<PendingPromotionAnswer> CreateAsync(
        Guid userId, Role targetRole, string typedShortCode, Guid? createdBy, CancellationToken ct)
    {
        if (targetRole is not (Role.Admin or Role.Member or Role.Viewer))
            throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypes.RoleNotAllowed);

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Row lock on the user: two promotions at once end up as one pending row.
        var user = (await db.Users.FromSql($"SELECT * FROM users WHERE id = {userId} FOR UPDATE").ToListAsync(ct))
            .SingleOrDefault(u => u.DeletedAt is null)
            ?? throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypes.NotFound);
        if (user.Kind != UserKind.Guest)
            throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypes.RoleNotAllowed);
        if (ShortCodeHelper.Normalize(typedShortCode) != user.ShortCode)
            throw ProblemException.Of(StatusCodes.Status400BadRequest, ProblemTypes.ShortCodeMismatch);

        await EndPendingAsync(userId, Promotion.Cancelled, now, ct);
        var promotion = new Promotion
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            TargetRole = targetRole,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = now + AuthLimitsHelper.PromotionLifetime,
        };
        db.Promotions.Add(promotion);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        LogCreated(log, userId, targetRole.ToWire());
        return new PendingPromotionAnswer(targetRole.ToWire(), promotion.ExpiresAt);
    }

    /// <summary>Cancels the pending promotion; 404 no-promotion if there is none.</summary>
    public async Task CancelAsync(Guid userId, CancellationToken ct)
    {
        if (await EndPendingAsync(userId, Promotion.Cancelled, time.GetUtcNow(), ct) == 0)
            throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypes.NoPromotion);
    }

    /// <summary>The pending, not expired promotion of the user, if any.</summary>
    public async Task<PendingPromotionAnswer?> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var p = await db.Promotions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId && x.EndedAt == null && x.ExpiresAt > now, ct);
        return p is null ? null : new PendingPromotionAnswer(p.TargetRole.ToWire(), p.ExpiresAt);
    }

    /// <summary>
    /// <c>POST /auth/complete-promotion</c> for the calling Guest: sets the username and password, applies the role,
    /// and rotates the refresh token of the same session.
    /// </summary>
    public async Task<TokenAnswer> CompleteAsync(Guid userId, Guid sessionId, CompletePromotionRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await users.FindByIdAsync(userId.ToString());
        var promotion = await db.Promotions.SingleOrDefaultAsync(p => p.UserId == userId && p.EndedAt == null, ct);
        if (user is null || user.Kind != UserKind.Guest || promotion is null)
            throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypes.NoPromotion);
        if (promotion.ExpiresAt <= now)
        {
            promotion.EndedAt = now;
            promotion.EndReason = Promotion.Expired;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw ProblemException.Of(StatusCodes.Status410Gone, ProblemTypes.PromotionExpired);
        }

        user.Kind = UserKind.Full;
        user.Role = promotion.TargetRole;
        user.LockoutEnabled = true;
        AccountsHelper.ThrowIfFailed(await users.SetUserNameAsync(user, request.Username));
        AccountsHelper.ThrowIfFailed(await users.AddPasswordAsync(user, request.Password));
        promotion.EndedAt = now;
        promotion.EndReason = Promotion.Completed;
        await db.SaveChangesAsync(ct);
        var refresh = await sessions.RotateAsync(sessionId, ct);
        await tx.CommitAsync(ct);
        LogCompleted(log, userId, user.Role.ToWire());
        return sessions.Answer(user, sessionId, refresh);
    }

    private Task<int> EndPendingAsync(Guid userId, string reason, DateTimeOffset now, CancellationToken ct) =>
        db.Promotions.Where(p => p.UserId == userId && p.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now).SetProperty(x => x.EndReason, reason), ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Promotion of Guest {UserId} to {Role} created")]
    private static partial void LogCreated(ILogger log, Guid userId, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Guest {UserId} completed the promotion to {Role}")]
    private static partial void LogCompleted(ILogger log, Guid userId, string role);
}
