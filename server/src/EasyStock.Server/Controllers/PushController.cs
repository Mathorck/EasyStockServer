using EasyStock.Server.Helpers;
using EasyStock.Server.Filters;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Server.Controllers;

[ApiController]
[Route("api/v1/push")]
public sealed class PushController(EasyStockDbContext db, ICurrentUser currentUser, TimeProvider time) : ControllerBase
{
    /// <summary>Longest endpoint accepted (docs/04-server-implementation-plan.md, 1.2 #31).</summary>
    public const int MaxEndpointLength = 2048;

    /// <summary>Saves the UnifiedPush endpoint of the current device session (insert or replace).</summary>
    [HttpPost("register")]
    [RequirePermission(Permission.ViewStock)] // every signed-in role may register
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypesHelper.ContentType)]
    public async Task<IActionResult> Register([FromBody] PushRegisterRequest? request, CancellationToken ct)
    {
        var endpoint = request?.Endpoint;
        if (!IsAcceptableEndpoint(endpoint))
            return ProblemTypesHelper.Problem(StatusCodes.Status400BadRequest, ProblemTypesHelper.ValidationFailed,
                "Invalid push endpoint",
                $"The endpoint must be an absolute http or https URL without user info, of at most {MaxEndpointLength} characters.");

        var sessionId = currentUser.SessionId;
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        // One statement, so two concurrent registrations of the same session cannot both insert.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO push_registration (device_session_id, endpoint, created_at, updated_at)
            VALUES ({sessionId}, {endpoint!}, {now}, {now})
            ON CONFLICT (device_session_id) DO UPDATE SET endpoint = excluded.endpoint, updated_at = excluded.updated_at
            """, ct);
        return NoContent();
    }

    /// <summary>Removes the UnifiedPush endpoint of the current device session. Idempotent: 204 even if none was saved.</summary>
    [HttpDelete("register")]
    [RequirePermission(Permission.ViewStock)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unregister(CancellationToken ct)
    {
        var sessionId = currentUser.SessionId;
        await db.Set<PushRegistration>().Where(r => r.DeviceSessionId == sessionId).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    private static bool IsAcceptableEndpoint(string? endpoint) =>
        endpoint is { Length: > 0 and <= MaxEndpointLength }
        && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.IsWellFormedOriginalString()
        // http and LAN hosts stay allowed: a self-hosted ntfy on the home network is a normal setup.
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0;
}
