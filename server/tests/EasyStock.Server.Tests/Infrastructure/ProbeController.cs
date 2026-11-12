using EasyStock.Server.Filters;
using EasyStock.Server.Enums;
using EasyStock.Server.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Tests.Infrastructure;

/// <summary>Test-only endpoints guarded by permissions (added to the test host as an application part).</summary>
[ApiController]
[Route("api/v1/test-probe")]
public sealed class ProbeController : ControllerBase
{
    [HttpGet("administer")]
    [RequirePermission(Permission.Administer)]
    public IActionResult Administer() => Ok();

    [HttpGet("view-stock")]
    [RequirePermission(Permission.ViewStock)]
    public IActionResult ViewStock() => Ok();

    [HttpGet("whoami")]
    [RequireSession]
    public IActionResult WhoAmI([FromServices] ICurrentUser current) =>
        Ok(new { userId = current.UserId, sessionId = current.SessionId, role = current.Role.ToString(), current.IsGuest });
}
