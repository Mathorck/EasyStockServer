using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.DB;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// The printed QR code that lets anyone create a Guest account. The Admin endpoints call Get, Update and
/// Regenerate; the token can never carry a role above Guest.
/// </summary>
public sealed partial class PublicInviteHandler(
    EasyStockDbContext db,
    SessionHandler sessions,
    GuestSignupCounterHandler counter,
    QrContentHandler qr,
    TimeProvider time,
    ILogger<PublicInviteHandler> log)
{
    public async Task<PublicInviteAnswer> GetAsync(CancellationToken ct)
    {
        var row = await db.PublicInvites.AsNoTracking().SingleOrDefaultAsync(ct) ?? new PublicInvite();
        return new PublicInviteAnswer(row.Enabled, row.GuestLimit, await CountGuestsAsync(ct),
            row.TokenHash is null ? null : row.CreatedAt);
    }

    /// <summary>Turns signup on or off and sets the Guest limit (null: no limit).</summary>
    public async Task<PublicInviteAnswer> UpdateAsync(bool enabled, int? guestLimit, CancellationToken ct)
    {
        if (guestLimit is < 0)
            throw ProblemException.Validation("guestLimit", "The Guest limit cannot be negative.");
        await EnsureRowAsync(ct);
        await db.PublicInvites.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Enabled, enabled)
            .SetProperty(x => x.GuestLimit, guestLimit), ct);
        return await GetAsync(ct);
    }

    /// <summary>A new token: printed sheets with the old one stop working (invite-invalid).</summary>
    public async Task<PublicInviteAnswer> RegenerateAsync(CancellationToken ct)
    {
        await EnsureRowAsync(ct);
        var token = SecretTokensHelper.New();
        var hash = SecretTokensHelper.Hash(token);
        var now = time.GetUtcNow();
        await db.PublicInvites.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.TokenHash, hash)
            .SetProperty(x => x.CreatedAt, now), ct);
        LogRegenerated(log);
        return await GetAsync(ct) with { Token = token, Qr = qr.Build(QrContentHandler.Guest, token) };
    }

    /// <summary><c>POST /auth/guest-signup</c>.</summary>
    public async Task<TokenAnswer> SignupAsync(GuestSignupRequest request, string ipKey, CancellationToken ct)
    {
        var row = await db.PublicInvites.AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is not { Enabled: true })
            throw ProblemException.Of(StatusCodes.Status403Forbidden, ProblemTypes.GuestSignupOff);
        if (!SecretTokensHelper.Matches(request.PublicToken, row.TokenHash))
            throw ProblemException.Of(StatusCodes.Status404NotFound, ProblemTypes.InviteInvalid);
        var displayName = AccountsHelper.CleanDisplayName(request.DisplayName);
        AccountsHelper.CleanDeviceName(request.DeviceName);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(AuthLimitsHelper.GuestSignupLockKey, ct);
        if (row.GuestLimit is { } limit && await CountGuestsAsync(ct) >= limit)
            throw ProblemException.Of(StatusCodes.Status409Conflict, ProblemTypes.GuestLimitReached);
        if (!counter.CanSignUp(ipKey, out var retryAfter))
            throw new ProblemException(StatusCodes.Status429TooManyRequests, ProblemTypes.RateLimited, retryAfter: retryAfter);

        // Always role guest, kind guest. No username, no password, no lockout.
        var user = await AccountsHelper.NewUserAsync(db, time, displayName, Role.Guest, UserKind.Guest, ct);
        db.Users.Add(user);
        var answer = await sessions.CreateAsync(user, request.DeviceName, ct);
        counter.Record(ipKey);
        await tx.CommitAsync(ct);
        LogSignedUp(log, user.Id);
        return answer;
    }

    private Task<int> CountGuestsAsync(CancellationToken ct) =>
        db.Users.CountAsync(u => u.Kind == UserKind.Guest && u.DeletedAt == null, ct);

    private Task EnsureRowAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.Database.ExecuteSqlAsync($"""
            INSERT INTO public_invite (id, enabled, guest_limit, created_at)
            VALUES ({PublicInvite.SingletonId}, false, {AuthLimitsHelper.DefaultGuestLimit}, {now})
            ON CONFLICT (id) DO NOTHING
            """, ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Public Guest QR code regenerated")]
    private static partial void LogRegenerated(ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "Guest {UserId} signed up with the public QR code")]
    private static partial void LogSignedUp(ILogger log, Guid userId);
}
