using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using Microsoft.AspNetCore.Identity;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>Outcome of <see cref="PasswordAttemptHandler.CheckAsync"/>.</summary>
public enum PasswordAttempt
{
    Right,
    Wrong,
    /// <summary>This attempt was wrong and locked the account for this address (or everywhere).</summary>
    WrongAndLocked,
    /// <summary>The account was already locked for this address; the password was not judged.</summary>
    Locked,
}

/// <summary>
/// Checks a password against the failed-attempt limits of login and <c>POST /me/password</c>, shared by both and kept
/// by <see cref="PasswordAttemptLimiter"/>: 5 failures lock the account for 15 minutes for the client address they came
/// from, and many failures from any address lock it everywhere. Identity's own lockout columns are not used: a
/// per-account lock let anyone who knows a username lock its owner out.
/// </summary>
public sealed class PasswordAttemptHandler(
    UserManager<AppUser> users, PasswordAttemptLimiter limiter, IHttpContextAccessor http)
{
    private static string? _dummyHash;

    /// <summary>When the lock that refused (or was set by) the attempt ends, as last read by <see cref="CheckAsync"/>.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public async Task<PasswordAttempt> CheckAsync(AppUser user, string password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var address = http.HttpContext is { } ctx ? ClientIp.Key(ctx) : ClientIp.Key((System.Net.IPAddress?)null);
        var (ticket, until) = user.LockoutEnabled ? limiter.TryBegin(user.Id, address) : (null, null);
        LockedUntil = until;
        if (user.LockoutEnabled && ticket is null)
        {
            // Same work as a real check, so the timing does not tell that the account is locked.
            VerifyDummy(password);
            return PasswordAttempt.Locked;
        }

        if (await users.CheckPasswordAsync(user, password))
        {
            if (ticket is { } t) limiter.Succeeded(t);
            LockedUntil = null;
            return PasswordAttempt.Right;
        }
        return ticket is { Locked: true } ? PasswordAttempt.WrongAndLocked : PasswordAttempt.Wrong;
    }

    /// <summary>Hashing work for a user that cannot be checked, so the timing matches a real check.</summary>
    public void VerifyDummy(string password)
    {
        _dummyHash ??= users.PasswordHasher.HashPassword(new AppUser(), "not-a-password-at-all");
        users.PasswordHasher.VerifyHashedPassword(new AppUser(), _dummyHash, password);
    }
}
