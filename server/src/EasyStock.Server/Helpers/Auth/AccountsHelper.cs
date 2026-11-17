using System.Globalization;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.DB;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Helpers.Auth;

/// <summary>Helpers shared by the account services.</summary>
public static class AccountsHelper
{
    public static string KindToWire(this UserKind kind) => kind == UserKind.Guest ? "guest" : "full";

    public static SessionUser ToSessionUser(this AppUser u) =>
        new(u.Id, u.DisplayName, u.Role.ToWire(), u.Kind.KindToWire(), ShortCodeHelper.Format(u.ShortCode));

    /// <summary>A new user with an ID whose short code is still free (the short code column is unique).</summary>
    public static async Task<AppUser> NewUserAsync(
        EasyStockDbContext db, TimeProvider time, string displayName, Role role, UserKind kind, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        while (true)
        {
            var id = Guid.CreateVersion7(now);
            var code = ShortCodeHelper.For(id);
            if (await db.Users.AnyAsync(u => u.ShortCode == code, ct)) continue;
            return new AppUser
            {
                Id = id,
                ShortCode = code,
                DisplayName = CleanDisplayName(displayName),
                Role = role,
                Kind = kind,
                CreatedAt = now,
                LockoutEnabled = kind == UserKind.Full,
                SecurityStamp = Guid.NewGuid().ToString("N"),
            };
        }
    }

    /// <summary>Trimmed, 1 to 50 characters, no control or format characters, otherwise 400 validation-failed.</summary>
    public static string CleanDisplayName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is < 1 or > AuthLimitsHelper.DisplayNameMaxLength)
            throw ProblemException.Validation("displayName", $"Display names are 1 to {AuthLimitsHelper.DisplayNameMaxLength} characters.");
        // Control (Cc) and format/bidi (Cf) characters could hide or reorder text in the admin screens.
        if (trimmed.Any(c => char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format))
            throw ProblemException.Validation("displayName", "Display names cannot contain control or formatting characters.");
        return trimmed;
    }

    public static string CleanDeviceName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length is >= 1 and <= AuthLimitsHelper.DeviceNameMaxLength
            ? trimmed
            : throw ProblemException.Validation("deviceName", $"Device names are 1 to {AuthLimitsHelper.DeviceNameMaxLength} characters.");
    }

    /// <summary>
    /// Turns an Identity failure into Problem Details: a taken username is 409 username-taken, anything else is
    /// 400 validation-failed with the messages under "username" or "password".
    /// </summary>
    public static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(e => e.Code == UsernameValidatorHandler.DuplicateCode))
            throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypesHelper.UsernameTaken);
        var errors = result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "username")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        throw new ProblemException(StatusCodes.Status400BadRequest, ProblemTypesHelper.ValidationFailed,
            extensions: new Dictionary<string, object?> { ["errors"] = errors });
    }

    /// <summary>Device name left on the sessions of a deleted account.</summary>
    public const string DeletedDeviceName = "Deleted device";

    /// <summary>
    /// Soft-deletes an account and erases its personal data (Admin delete, <c>DELETE /me</c>, Guest logout and the
    /// Guest cleanup). The row stays: movements point at it, and a hard delete would leave no <c>users.server_seq</c>
    /// bump, so other phones would keep the name for ever. The trigger bumps the seq when <c>deleted_at</c> and
    /// <c>display_name</c> change, and phones pull "Deleted user". Erased: username (null, so it is free again),
    /// display name, password hash, alert preference, last seen, lockout state, the device names of every session,
    /// every password reset of the user; a pending promotion is cancelled. Stock movements and low-stock alerts keep
    /// the user ID and are never touched. The caller revokes the sessions (which drops the push registrations) and
    /// owns the transaction.
    /// </summary>
    public static async Task EraseAccountAsync(EasyStockDbContext db, AppUser user, DateTimeOffset now, CancellationToken ct)
    {
        user.DeletedAt = now;
        user.DisplayName = Handlers.Admin.AdminUsersHandler.DeletedDisplayName;
        user.UserName = null;
        user.NormalizedUserName = null;
        user.PasswordHash = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.AlertsEnabled = null;
        user.LastSeenAt = null;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        await db.SaveChangesAsync(ct);
        var id = user.Id;
        await db.DeviceSessions.Where(s => s.UserId == id && s.DeviceName != DeletedDeviceName)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceName, DeletedDeviceName), ct);
        await db.Promotions.Where(p => p.UserId == id && p.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now).SetProperty(x => x.EndReason, Promotion.Cancelled), ct);
        await db.PasswordResets.Where(r => r.UserId == id).ExecuteDeleteAsync(ct);
    }

    /// <summary>An Admin that counts for the last-Admin rule: full account, not disabled, not deleted.</summary>
    public static bool IsActiveAdmin(AppUser u) =>
        u.Role == Role.Admin && u.Kind == UserKind.Full && u.DisabledAt is null && u.DeletedAt is null;

    /// <summary>409 last-admin unless another active Admin than <paramref name="id"/> exists. Call under <see cref="AuthLimitsHelper.AdminChangeLockKey"/>.</summary>
    public static async Task EnsureAnotherAdminAsync(EasyStockDbContext db, Guid id, CancellationToken ct)
    {
        var others = await db.Users.AnyAsync(u => u.Id != id && u.Role == Role.Admin && u.Kind == UserKind.Full
            && u.DisabledAt == null && u.DeletedAt == null, ct);
        if (!others) throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypesHelper.LastAdmin);
    }

    public static Task LockAsync(this EasyStockDbContext db, long key, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);

    public static ProblemException Unauthenticated(string type) => ProblemException.Of(StatusCodes.Status401Unauthorized, type);
}
