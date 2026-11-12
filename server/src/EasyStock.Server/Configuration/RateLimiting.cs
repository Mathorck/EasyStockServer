using System.Globalization;
using System.Net.Sockets;
using System.Net;
using System.Threading.RateLimiting;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Server.Configuration;

/// <summary>
/// Rate limits (docs/03-server-spec.md, "Rate limits"): <see cref="AuthIp"/> (10 per minute per IP) on login,
/// setup, invite redeem, Guest signup, reset and complete-promotion; every other /api call 120 per minute per
/// device session, or per IP without a valid token. Rejections are 429 <c>rate-limited</c> with Retry-After.
/// </summary>
public static class RateLimiting
{
    public const string AuthIp = "auth-ip";

    public static IServiceCollection AddEasyStockRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) =>
            {
                var wait = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromMinutes(1);
                var seconds = ProblemException.Seconds(wait);
                ctx.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                return new ValueTask(Results.Problem(
                    type: ProblemTypes.RateLimited,
                    title: ProblemTypes.DefaultTitle(ProblemTypes.RateLimited),
                    statusCode: StatusCodes.Status429TooManyRequests,
                    extensions: new Dictionary<string, object?> { ["retryAfter"] = seconds }).ExecuteAsync(ctx.HttpContext));
            };

            o.AddPolicy(AuthIp, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp.Key(ctx), _ => PerMinute(10)));

            // The default: a global limiter, so controllers added later are covered without any attribute.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                if (!ctx.Request.Path.StartsWithSegments("/api")
                    || ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == AuthIp)
                    return RateLimitPartition.GetNoLimiter("none");
                var sid = ctx.User.FindFirst(AccessTokenHandler.SessionClaim)?.Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    sid is null ? "ip:" + ClientIp.Key(ctx) : "sid:" + sid, _ => PerMinute(120));
            });
        });

    private static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}

/// <summary>The client address used for limits, after the forwarded-headers middleware.</summary>
public static class ClientIp
{
    /// <summary>IPv4 (also when mapped into IPv6) as is; IPv6 by /64, since one home gets a whole /64.</summary>
    public static string Key(HttpContext ctx) => Key(ctx.Connection.RemoteIpAddress);

    public static string Key(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
