using EasyStock.Server.Filters;
using EasyStock.Server.Configuration;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

/// <summary>The caller's own account.</summary>
[ApiController]
[Route("api/v1/me")]
[RequireSession]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypes.ContentType)]
public sealed class MeController(ICurrentUser current, AccountHandler accounts) : ControllerBase
{
    /// <summary>The user, role, kind, short code and the pending promotion if there is one.</summary>
    [HttpGet]
    [ProducesResponseType<MeAnswer>(StatusCodes.Status200OK)]
    public Task<MeAnswer> Get(CancellationToken ct) => accounts.GetAsync(current.UserId, ct);

    /// <summary>Changes the display name and the alert preference (Guests: the display name only).</summary>
    [HttpPatch]
    [ProducesResponseType<MeAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypes.ContentType)]
    public Task<MeAnswer> Update(UpdateMeRequest request, CancellationToken ct) =>
        accounts.UpdateAsync(current.UserId, request, ct);

    /// <summary>Changes the password of a full account; the other sessions end.</summary>
    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypes.ContentType)]
    public async Task<NoContentResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await accounts.ChangePasswordAsync(current.UserId, current.SessionId, request, ct);
        return NoContent();
    }
}
