using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Admin;

/// <summary>
/// User administration (<c>/admin/users</c>). Delete is a soft delete: stock movements keep <c>created_by</c>
/// and the phones still pull the row (display name "Deleted user"). The last active Admin (full account, not
/// disabled, not deleted) cannot be demoted, disabled or deleted; those changes run under an advisory lock so two
/// Admins cannot remove each other at the same time.
/// </summary>
public sealed partial class AdminUsersHandler(
    EasyStockDbContext db,
    SessionHandler sessions,
    TimeProvider time,
    ILogger<AdminUsersHandler> log)
{
    public const string DeletedDisplayName = "Deleted user";

    public async Task<IReadOnlyList<AdminUserAnswer>> ListAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var users = await db.Users.AsNoTracking().Where(u => u.DeletedAt == null)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id).ToListAsync(ct);
        var devices = (await db.DeviceSessions.AsNoTracking().Where(s => s.RevokedAt == null)
                .OrderByDescending(s => s.LastUsedAt)
                .Select(s => new { s.UserId, Device = new AdminUserDevice(s.Id, s.DeviceName, s.LastUsedAt) })
                .ToListAsync(ct))
            .ToLookup(x => x.UserId, x => x.Device);
        var promotions = (await db.Promotions.AsNoTracking().Where(p => p.EndedAt == null && p.ExpiresAt > now)
                .ToListAsync(ct))
            .ToDictionary(p => p.UserId, p => new PendingPromotionAnswer(p.TargetRole.ToWire(), p.ExpiresAt));
        return users.Select(u => new AdminUserAnswer(
            u.Id, u.DisplayName, u.UserName, u.Role.ToWire(), u.Kind.KindToWire(), ShortCodeHelper.Format(u.ShortCode),
            u.CreatedAt, u.LastSeenAt, u.DisabledAt is not null,
            devices[u.Id].ToList(), promotions.GetValueOrDefault(u.Id))).ToList();
    }

    /// <summary>
    /// 404 not-found, 400 validation-failed (bad role or display name), 409 role-not-allowed (a Guest changes role
    /// through a promotion, a full account never becomes a Guest), 409 last-admin.
    /// </summary>
    public async Task<AdminUserAnswer> UpdateAsync(Guid id, UpdateAdminUserRequest request, CancellationToken ct)
    {
        Role? role = null;
        if (request.Role is not null)
            role = Permissions.ParseRole(request.Role) is { } r
                ? r
                : throw ProblemException.Validation("role", "Unknown role.");
        var displayName = request.DisplayName is null ? null : AccountsHelper.CleanDisplayName(request.DisplayName);

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AuthLimitsHelper.AdminChangeLockKey, ct);
        var user = await FindAsync(id, ct);

        if (role is { } newRole && newRole != user.Role)
        {
            if (user.Kind == UserKind.Guest || newRole == Role.Guest)
                throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypesHelper.RoleNotAllowed);
            if (IsActiveAdmin(user)) await EnsureAnotherAdminAsync(id, ct);
            user.Role = newRole;
        }
        else if (role == Role.Guest && user.Kind != UserKind.Guest)
            throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypesHelper.RoleNotAllowed);

        if (displayName is not null) user.DisplayName = displayName;

        var disabledNow = false;
        if (request.Disabled is true && user.DisabledAt is null)
        {
            if (IsActiveAdmin(user)) await EnsureAnotherAdminAsync(id, ct);
            user.DisabledAt = now;
            disabledNow = true;
        }
        else if (request.Disabled is false)
            user.DisabledAt = null;

        await db.SaveChangesAsync(ct);
        if (disabledNow) await sessions.RevokeAllAsync(id, AuthLimitsHelper.RevokeReasons.Disabled, keep: null, ct);
        await tx.CommitAsync(ct);
        LogUpdated(log, id);
        return await GetAsync(id, ct);
    }

    /// <summary>Soft delete. 404 not-found, 409 last-admin.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AuthLimitsHelper.AdminChangeLockKey, ct);
        var user = await FindAsync(id, ct);
        if (IsActiveAdmin(user)) await EnsureAnotherAdminAsync(id, ct);

        // The row stays (movements point at it, phones pull it); everything that identifies or lets the person in goes.
        user.DeletedAt = now;
        user.DisplayName = DeletedDisplayName;
        user.UserName = null;
        user.NormalizedUserName = null;
        user.PasswordHash = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.AlertsEnabled = null;
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(id, AuthLimitsHelper.RevokeReasons.Deleted, keep: null, ct);
        await db.Promotions.Where(p => p.UserId == id && p.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now).SetProperty(x => x.EndReason, Promotion.Cancelled), ct);
        await db.PasswordResets.Where(r => r.UserId == id && r.UsedAt == null).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        LogDeleted(log, id);
    }

    private async Task<AdminUserAnswer> GetAsync(Guid id, CancellationToken ct) =>
        (await ListAsync(ct)).Single(u => u.Id == id);

    private async Task<AppUser> FindAsync(Guid id, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.DeletedAt == null, ct)
        ?? throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypesHelper.NotFound);

    private static bool IsActiveAdmin(AppUser u) =>
        u.Role == Role.Admin && u.Kind == UserKind.Full && u.DisabledAt is null && u.DeletedAt is null;

    private async Task EnsureAnotherAdminAsync(Guid id, CancellationToken ct)
    {
        var others = await db.Users.AnyAsync(u => u.Id != id && u.Role == Role.Admin && u.Kind == UserKind.Full
            && u.DisabledAt == null && u.DeletedAt == null, ct);
        if (!others) throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypesHelper.LastAdmin);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin changed user {UserId}")]
    private static partial void LogUpdated(ILogger log, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin deleted user {UserId}")]
    private static partial void LogDeleted(ILogger log, Guid userId);
}
