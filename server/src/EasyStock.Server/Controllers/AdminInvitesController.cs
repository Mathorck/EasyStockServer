using EasyStock.Server.Filters;
using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

/// <summary>
/// Admin: single-use invites for full accounts (admin, member, viewer). Guests never come from an invite: they come
/// from the printed public QR code. Errors are Problem Details (docs/04-server-implementation-plan.md, 3.6).
/// </summary>
[ApiController]
[Route("api/v1/admin/invites")]
[RequirePermission(Permission.Administer)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypes.ContentType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypes.ContentType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypes.ContentType)]
public sealed class AdminInvitesController(ICurrentUser current, InviteHandler invites) : ControllerBase
{
    /// <summary>Creates an invite (7 days, single use). The answer carries the <c>qr</c> content with the token.</summary>
    [HttpPost]
    [ProducesResponseType<InviteAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    public Task<InviteAnswer> Create(CreateInviteRequest request, CancellationToken ct) =>
        invites.CreateAsync(ParseInvitableRole(request.Role), current.UserId, ct);

    /// <summary>Open invites (not used, not expired), oldest first. The tokens are not returned again.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InviteAnswer>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<InviteAnswer>> List(CancellationToken ct) => invites.ListOpenAsync(ct);

    /// <summary>Cancels an unused invite. 404 not-found if it is unknown, already used or cancelled.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    public async Task<NoContentResult> Cancel(Guid id, CancellationToken ct)
    {
        await invites.CancelAsync(id, ct);
        return NoContent();
    }

    /// <summary>400 validation-failed for an unknown or missing role. A Guest role passes: the handler answers 409 role-not-allowed.</summary>
    private static Role ParseInvitableRole(string? wire) =>
        Permissions.ParseRole(wire)
        ?? throw ProblemException.Validation("role", "The role must be admin, member or viewer.");
}
