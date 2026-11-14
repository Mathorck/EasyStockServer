using EasyStock.Server.Helpers;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// Failed-password counters of login and <c>POST /me/password</c>, kept in memory (no IP address is ever stored in
/// the database, docs/03-server-spec.md "Personal data").
/// <list type="bullet">
/// <item>Per account and client address (<see cref="Configuration.ClientIp.Key(Microsoft.AspNetCore.Http.HttpContext)"/>):
/// <see cref="AuthLimitsHelper.MaxFailedLogins"/> failures lock that pair for <see cref="AuthLimitsHelper.LockoutDuration"/>.
/// Someone who only knows a username can no longer lock its owner out from another address.</item>
/// <item>Per account, from any address: <see cref="AuthLimitsHelper.MaxFailedLoginsPerAccount"/> failures within
/// <see cref="AuthLimitsHelper.LockoutDuration"/> lock the account everywhere, so guessing spread over many addresses stays
/// bounded.</item>
/// </list>
/// An attempt is counted as a failure before the password is judged and given back when it was right, so parallel
/// guesses cannot get past a limit. Counters are lost on restart, which an attacker cannot cause.
/// </summary>
public sealed class PasswordAttemptLimiter(TimeProvider time)
{
    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset WindowStart;
        public DateTimeOffset LastFailure;
        public DateTimeOffset? LockedUntil;
    }

    /// <summary>A reserved attempt; give it back with <see cref="Succeeded"/> when the password was right.</summary>
    public readonly record struct Ticket(Guid UserId, string Address, bool Locked);

    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid UserId, string Address), Counter> _pairs = [];
    private readonly Dictionary<Guid, Counter> _accounts = [];
    private int _sinceSweep;

    /// <summary>
    /// Reserves an attempt of <paramref name="userId"/> from <paramref name="address"/>, or returns when the lock that
    /// refuses it ends. A reserved attempt that reaches a limit is <see cref="Ticket.Locked"/>.
    /// </summary>
    public (Ticket? Ticket, DateTimeOffset? LockedUntil) TryBegin(Guid userId, string address)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (++_sinceSweep >= 256) Sweep(now);
            var pair = Get(_pairs, (userId, address), now);
            var account = Get(_accounts, userId, now);
            var until = Max(Active(pair, now), Active(account, now));
            if (until is not null) return (null, until);

            var locked = Fail(pair, AuthLimitsHelper.MaxFailedLogins, now) | Fail(account, AuthLimitsHelper.MaxFailedLoginsPerAccount, now);
            return (new Ticket(userId, address, locked), locked ? Max(pair.LockedUntil, account.LockedUntil) : null);
        }
    }

    /// <summary>The password was right: the pair starts over and the account gets the reserved failure back.</summary>
    public void Succeeded(Ticket ticket)
    {
        lock (_gate)
        {
            _pairs.Remove((ticket.UserId, ticket.Address));
            if (_accounts.TryGetValue(ticket.UserId, out var account))
            {
                account.Failures = Math.Max(0, account.Failures - 1);
                if (account.Failures < AuthLimitsHelper.MaxFailedLoginsPerAccount) account.LockedUntil = null;
            }
        }
    }

    /// <summary>Forgets every counter of <paramref name="userId"/> (its password was reset).</summary>
    public void Clear(Guid userId)
    {
        lock (_gate)
        {
            _accounts.Remove(userId);
            foreach (var key in _pairs.Keys.Where(k => k.UserId == userId).ToList()) _pairs.Remove(key);
        }
    }

    private static Counter Get<TKey>(Dictionary<TKey, Counter> map, TKey key, DateTimeOffset now) where TKey : notnull
    {
        if (map.TryGetValue(key, out var c) && !Expired(c, now)) return c;
        return map[key] = new Counter { WindowStart = now };
    }

    /// <summary>A lock is over, or no failure for a whole window: the counter starts over.</summary>
    private static bool Expired(Counter c, DateTimeOffset now) => c.LockedUntil is { } until
        ? until <= now
        : now - c.WindowStart >= AuthLimitsHelper.LockoutDuration && now - c.LastFailure >= AuthLimitsHelper.LockoutDuration;

    private static DateTimeOffset? Active(Counter c, DateTimeOffset now) => c.LockedUntil is { } until && until > now ? until : null;

    private static bool Fail(Counter c, int max, DateTimeOffset now)
    {
        // Failures older than a window no longer count.
        if (c.Failures > 0 && now - c.WindowStart >= AuthLimitsHelper.LockoutDuration)
            (c.Failures, c.WindowStart) = (0, now);
        if (c.Failures == 0) c.WindowStart = now;
        c.Failures++;
        c.LastFailure = now;
        if (c.Failures < max) return false;
        c.LockedUntil = now + AuthLimitsHelper.LockoutDuration;
        return true;
    }

    private void Sweep(DateTimeOffset now)
    {
        _sinceSweep = 0;
        foreach (var key in _pairs.Where(p => Expired(p.Value, now)).Select(p => p.Key).ToList()) _pairs.Remove(key);
        foreach (var key in _accounts.Where(p => Expired(p.Value, now)).Select(p => p.Key).ToList()) _accounts.Remove(key);
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;
}
