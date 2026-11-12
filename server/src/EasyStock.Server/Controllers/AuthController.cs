using EasyStock.Server.Filters;
using EasyStock.Server.Configuration;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Server.Controllers;

/// <summary>
/// Sessions: setup, invite, Guest signup, login, refresh, reset, promotion, logout. Errors are Problem Details
/// (docs/04-server-implementation-plan.md, 3.6).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypes.ContentType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, ProblemTypes.ContentType)]
public sealed class AuthController : ControllerBase
{
    /// <summary>Creates the first Admin with the setup token printed at startup. 409 setup-done afterwards.</summary>
    [HttpPost("setup")]
    [EnableRateLimiting(RateLimiting.AuthIp)]
    [ProducesResponseType<TokenAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    public Task<TokenAnswer> Setup(SetupRequest request, [FromServices] SetupHandler setup, CancellationToken ct) =>
        setup.CompleteAsync(request, ct);

    /// <summary>Creates a full account from an invite (single use, 7 days).</summary>
    [HttpPost("invite/redeem")]
    [EnableRateLimiting(RateLimiting.AuthIp)]
    [ProducesResponseType<TokenAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone, ProblemTypes.ContentType)]
    public Task<TokenAnswer> RedeemInvite(RedeemInviteRequest request, [FromServices] InviteHandler invites, CancellationToken ct) =>
        invites.RedeemAsync(request, ct);

    /// <summary>Creates a Guest account from the printed public QR code.</summary>
    [HttpPost("guest-signup")]
    [EnableRateLimiting(RateLimiting.AuthIp)]
    [ProducesResponseType<TokenAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    public Task<TokenAnswer> GuestSignup(GuestSignupRequest request, [FromServices] PublicInviteHandler publicInvite, CancellationToken ct) =>
        publicInvite.SignupAsync(request, ClientIp.Key(HttpContext), ct);

    /// <summary>Opens a session with a username and password. 5 failures lock the account for 15 minutes.</summary>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimiting.AuthIp)]
    [ProducesResponseType<TokenAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypes.ContentType)]
    public Task<TokenAnswer> Login(LoginRequest request, [FromServices] LoginHandler login, CancellationToken ct) =>
        login.LoginAsync(request, ct);

    /// <summary>New access and refresh tokens. Presenting an already used refresh token ends the session.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<TokenAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypes.ContentType)]
    public Task<TokenAnswer> Refresh(RefreshRequest request, [FromServices] SessionHandler sessions, CancellationToken ct) =>
        sessions.RefreshAsync(request.RefreshToken, ct);

    /// <summary>Sets a new password with a reset code from an Admin; every other session ends.</summary>
    [HttpPost("reset")]
    [EnableRateLimiting(RateLimiting.AuthIp)]
    [ProducesResponseType<TokenAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone, ProblemTypes.ContentType)]
    public Task<TokenAnswer> Reset(ResetPasswordRequest request, [FromServices] PasswordResetHandler resets, CancellationToken ct) =>
        resets.RedeemAsync(request, ct);

    /// <summary>A promoted Guest chooses a username and password and gets the new role, on the same session.</summary>
    [HttpPost("complete-promotion")]
    [RequireSession]
    [EnableRateLimiting(RateLimiting.AuthIp)]
    [ProducesResponseType<TokenAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemTypes.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone, ProblemTypes.ContentType)]
    public Task<TokenAnswer> CompletePromotion(
        CompletePromotionRequest request,
        [FromServices] ICurrentUser current,
        [FromServices] PromotionHandler promotions,
        CancellationToken ct) =>
        promotions.CompleteAsync(current.UserId, current.SessionId, request, ct);

    /// <summary>Ends the current session. A Guest account is deleted.</summary>
    [HttpPost("logout")]
    [RequireSession]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemTypes.ContentType)]
    public async Task<NoContentResult> Logout(
        [FromServices] ICurrentUser current, [FromServices] AccountHandler accounts, CancellationToken ct)
    {
        await accounts.LogoutAsync(current.UserId, current.SessionId, ct);
        return NoContent();
    }
}
