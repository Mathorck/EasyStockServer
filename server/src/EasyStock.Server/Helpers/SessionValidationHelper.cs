using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.Enums;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Server.Helpers;

/// <summary>
/// JWT bearer events (docs/04-server-implementation-plan.md, 3.4). A valid signature is not enough: the session
/// and the user are read from the database on every request, so revoking or disabling takes effect at once,
/// and <see cref="ICurrentUser"/> gets the role from the database. A refused token leaves the request
/// anonymous; the reason is kept for the 401 answer (<see cref="AuthFailureHelper"/>).
/// </summary>
public static class SessionValidationHelper
{
    public static JwtBearerEvents Events() => new()
    {
        OnTokenValidated = OnTokenValidatedAsync,
        OnAuthenticationFailed = ctx =>
        {
            AuthFailureHelper.Record(ctx.HttpContext,
                ctx.Exception is SecurityTokenExpiredException ? ProblemTypes.TokenExpired : ProblemTypes.Unauthorized);
            return Task.CompletedTask;
        },
        OnChallenge = async ctx =>
        {
            ctx.HandleResponse();
            await AuthFailureHelper.Unauthenticated(ctx.HttpContext).ExecuteResultAsync(new() { HttpContext = ctx.HttpContext });
        },
        OnForbidden = ctx => ProblemTypes.Problem(StatusCodes.Status403Forbidden, ProblemTypes.NotAllowed)
            .ExecuteResultAsync(new() { HttpContext = ctx.HttpContext }),
    };

    private static async Task OnTokenValidatedAsync(TokenValidatedContext ctx)
    {
        var principal = ctx.Principal;
        if (!Guid.TryParse(principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
            || !Guid.TryParse(principal?.FindFirst(AccessTokenHandler.SessionClaim)?.Value, out var sessionId))
        {
            Fail(ctx, ProblemTypes.Unauthorized);
            return;
        }

        var services = ctx.HttpContext.RequestServices;
        var db = services.GetRequiredService<EasyStockDbContext>();
        var ct = ctx.HttpContext.RequestAborted;
        var row = await db.DeviceSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .Select(s => new
            {
                s.RevokedAt,
                s.LastUsedAt,
                s.User.Role,
                s.User.Kind,
                s.User.DisabledAt,
                s.User.DeletedAt,
                s.User.LastSeenAt,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.RevokedAt is not null)
        {
            Fail(ctx, ProblemTypes.SessionRevoked);
            return;
        }
        if (row.DisabledAt is not null || row.DeletedAt is not null)
        {
            Fail(ctx, ProblemTypes.AccountDisabled);
            return;
        }

        services.GetRequiredService<CurrentUserHandler>().Set(userId, sessionId, row.Role, row.Kind == UserKind.Guest);

        // Throttled activity writes; the WHERE clause keeps concurrent requests from writing twice.
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var before = now - AuthLimitsHelper.ActivityWriteInterval;
        if (row.LastUsedAt < before)
            await db.DeviceSessions.Where(s => s.Id == sessionId && s.LastUsedAt < before)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, now), ct);
        if (row.LastSeenAt is null || row.LastSeenAt < before)
            await db.Users.Where(u => u.Id == userId && (u.LastSeenAt == null || u.LastSeenAt < before))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), ct);
    }

    private static void Fail(TokenValidatedContext ctx, string type)
    {
        AuthFailureHelper.Record(ctx.HttpContext, type);
        ctx.Fail(type);
    }
}
