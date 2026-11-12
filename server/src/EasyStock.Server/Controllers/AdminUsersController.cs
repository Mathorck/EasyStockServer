using EasyStock.Server.Filters;
using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Admin;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

/// <summary>User administration. Admin only.</summary>
[ApiController]
[Route("api/v1/admin/users")]
[RequirePermission(Permission.Administer)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypes.ContentType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypes.ContentType)]
public sealed class AdminUsersController(
    ICurrentUser current,
    AdminUsersHandler users,
    PasswordResetHandler resets,
    PromotionHandler promotions) : ControllerBase
{
    /// <summary>Users with role, kind, last seen, live devices and pending promotion (deleted users are left out).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminUserAnswer>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AdminUserAnswer>> List(CancellationToken ct) => users.ListAsync(ct);

    /// <summary>Changes role, display name, or disables or enables the user (disabling ends their sessions).</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<AdminUserAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    public Task<AdminUserAnswer> Update(Guid id, UpdateAdminUserRequest request, CancellationToken ct) =>
        users.UpdateAsync(id, request, ct);

    /// <summary>Deletes the user. Their movements keep the ID and show "Deleted user".</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    public async Task<NoContentResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>A single-use password reset token (24 hours) and its QR content. Not for Guests.</summary>
    [HttpPost("{id:guid}/reset")]
    [ProducesResponseType<QrGrant>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    public Task<QrGrant> Reset(Guid id, CancellationToken ct) => resets.CreateAsync(id, current.UserId, ct);

    /// <summary>Promotes a Guest to a role; the Admin types the short code shown on the Guest's phone.</summary>
    [HttpPost("{id:guid}/promote")]
    [ProducesResponseType<PendingPromotionAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    public Task<PendingPromotionAnswer> Promote(Guid id, PromoteUserRequest request, CancellationToken ct)
    {
        var role = Permissions.ParseRole(request.Role)
            ?? throw ProblemException.Validation("role", "Unknown role.");
        return promotions.CreateAsync(id, role, request.ShortCode, current.UserId, ct);
    }

    /// <summary>Cancels the pending promotion.</summary>
    [HttpDelete("{id:guid}/promote")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    public async Task<NoContentResult> CancelPromotion(Guid id, CancellationToken ct)
    {
        await promotions.CancelAsync(id, ct);
        return NoContent();
    }
}
