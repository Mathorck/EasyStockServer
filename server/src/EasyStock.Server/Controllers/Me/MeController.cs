using EasyStock.Server.Configuration;
using EasyStock.Server.Filters;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Server.Controllers.Me;

/// <summary>The caller's own account.</summary>
[ApiController]
[Route("api/v1/me")]
[RequireSession]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypesHelper.ContentType)]
public sealed class MeController(ICurrentUser current, AccountHandler accounts, PersonalDataHandler personalData) : ControllerBase
{
    /// <summary>The user, role, kind, short code and the pending promotion if there is one.</summary>
    [HttpGet]
    [ProducesResponseType<MeAnswer>(StatusCodes.Status200OK)]
    public Task<MeAnswer> Get(CancellationToken ct) => accounts.GetAsync(current.UserId, ct);

    /// <summary>Changes the display name and the alert preference (Guests: the display name only).</summary>
    [HttpPatch]
    [ProducesResponseType<MeAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypesHelper.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypesHelper.ContentType)]
    public Task<MeAnswer> Update(UpdateMeRequest request, CancellationToken ct) =>
        accounts.UpdateAsync(current.UserId, request, ct);

    /// <summary>Changes the password of a full account; the other sessions end.</summary>
    [HttpPost("password")]
    [EnableRateLimiting(RateLimiting.AuthIp)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypesHelper.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypesHelper.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, ProblemTypesHelper.ContentType)]
    public async Task<NoContentResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await accounts.ChangePasswordAsync(current.UserId, current.SessionId, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes the caller's own account and erases its personal data: username (free again), display name ("Deleted
    /// user" on the phones), password, alert preference, last seen, device names, sessions and push registrations.
    /// Movements keep the user ID. Accounts with a password must send it (400 invalid-credentials when wrong or
    /// missing, 429 rate-limited while locked); Guests have none and may send no body. 409 last-admin for the last
    /// active Admin.
    /// </summary>
    [HttpDelete]
    [EnableRateLimiting(RateLimiting.AuthIp)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypesHelper.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypesHelper.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, ProblemTypesHelper.ContentType)]
    public async Task<NoContentResult> Delete(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteMeRequest? request, CancellationToken ct)
    {
        await accounts.DeleteSelfAsync(current.UserId, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Downloads the caller's personal data as a JSON file (every role, Guests included): account, devices, sessions,
    /// push registrations (endpoint host only) and the movements they made. No password hash, token or other
    /// user's data. 5 per hour per user.
    /// </summary>
    [HttpGet("data")]
    [EnableRateLimiting(RateLimiting.MeData)]
    [Produces("application/json")]
    [ProducesResponseType<PersonalDataExport>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, ProblemTypesHelper.ContentType)]
    public async Task<EmptyResult> Data(CancellationToken ct)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/json; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{personalData.FileName()}\"";
        Response.Headers.CacheControl = "no-store";
        await personalData.WriteAsync(current.UserId, Response.Body, ct);
        return new EmptyResult();
    }
}
