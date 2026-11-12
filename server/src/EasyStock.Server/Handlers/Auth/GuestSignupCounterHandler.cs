using EasyStock.Server.Helpers;

namespace EasyStock.Server.Handlers.Auth;

/// <summary>
/// Successful Guest signups per client IP over the last hour (sliding window), in memory only: no IP address is
/// ever stored in the database. Restarting the server clears it.
/// </summary>
public sealed class GuestSignupCounterHandler(TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _byIp = [];
    private readonly Lock _gate = new();

    /// <summary>False when <paramref name="ipKey"/> already has the maximum; <paramref name="retryAfter"/> says when a slot frees up.</summary>
    public bool CanSignUp(string ipKey, out TimeSpan retryAfter)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            retryAfter = TimeSpan.Zero;
            if (!_byIp.TryGetValue(ipKey, out var times)) return true;
            Prune(times, now);
            if (times.Count < AuthLimitsHelper.GuestSignupsPerIpPerHour) return true;
            retryAfter = times.Peek() + Window - now;
            return false;
        }
    }

    public void Record(string ipKey)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (!_byIp.TryGetValue(ipKey, out var times)) _byIp[ipKey] = times = new Queue<DateTimeOffset>();
            Prune(times, now);
            times.Enqueue(now);
            // Forget the IPs whose window is empty, so the map does not grow forever.
            foreach (var (ip, q) in _byIp.ToList())
            {
                Prune(q, now);
                if (q.Count == 0) _byIp.Remove(ip);
            }
        }
    }

    private static void Prune(Queue<DateTimeOffset> times, DateTimeOffset now)
    {
        while (times.Count > 0 && times.Peek() + Window <= now) times.Dequeue();
    }
}
