using EasyStock.Server.Filters;
using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

/// <summary>The printed Guest QR code: its state, on/off and Guest limit, and a new code.</summary>
[ApiController]
[Route("api/v1/admin/public-invite")]
[RequirePermission(Permission.Administer)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypes.ContentType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypes.ContentType)]
public sealed class AdminPublicInviteController(PublicInviteHandler publicInvites) : ControllerBase
{
    /// <summary>The state of the printed QR code: on or off, the Guest limit, the Guest count and when the code was made.</summary>
    [HttpGet]
    [ProducesResponseType<PublicInviteAnswer>(StatusCodes.Status200OK)]
    public Task<PublicInviteAnswer> Get(CancellationToken ct) => publicInvites.GetAsync(ct);

    /// <summary>Turns the printed QR code on or off and sets the Guest limit (absent: no limit).</summary>
    [HttpPut]
    [ProducesResponseType<PublicInviteAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypes.ContentType)]
    public Task<PublicInviteAnswer> Update(UpdatePublicInviteRequest request, CancellationToken ct) =>
        publicInvites.UpdateAsync(request.Enabled!.Value, request.GuestLimit, ct);

    /// <summary>A new token. Printed sheets with the old one stop working. The answer carries the new QR content, once.</summary>
    [HttpPost("regenerate")]
    [ProducesResponseType<PublicInviteAnswer>(StatusCodes.Status200OK)]
    public Task<PublicInviteAnswer> Regenerate(CancellationToken ct) => publicInvites.RegenerateAsync(ct);
}
