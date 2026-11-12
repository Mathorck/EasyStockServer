using EasyStock.Server.Helpers;
using EasyStock.Server.Filters;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

/// <summary>Admin: every device session of every user, and revoking one of them.</summary>
[ApiController]
[Route("api/v1/admin/devices")]
[RequirePermission(Permission.Administer)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypesHelper.ContentType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypesHelper.ContentType)]
public sealed class AdminDevicesController(SessionHandler sessions) : ControllerBase
{
    /// <summary>Every session, revoked ones included, most recently used first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminDeviceAnswer>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AdminDeviceAnswer>> List(CancellationToken ct) => sessions.ListAllAsync(ct);

    /// <summary>Revokes one session: its access and refresh tokens stop working at once. Idempotent.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypesHelper.ContentType)]
    public async Task<NoContentResult> Revoke(Guid id, CancellationToken ct)
    {
        await sessions.RevokeByAdminAsync(id, ct);
        return NoContent();
    }
}
