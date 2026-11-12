using EasyStock.Server.Filters;
using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Settings;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

// Not [ApiController]: its automatic 400 uses the RFC 9110 type, but the API promises `validation-failed`.
// The body is checked by hand below, so every invalid request gets the same Problem Details.
[Route("api/v1/admin/settings")]
public sealed class AdminSettingsController(SettingsHandler store) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.Administer)]
    public async Task<ServerSettings> Get(CancellationToken ct) => await store.GetAsync(ct);

    [HttpPut]
    [RequirePermission(Permission.Administer)]
    public async Task<IActionResult> Put([FromBody] ServerSettings? settings, CancellationToken ct)
    {
        if (settings is null)
            return InvalidSettings("The request body must be a JSON object with serverName and movementAuthorRetentionDays.");
        if (!ModelState.IsValid)
            return InvalidSettings(FirstModelError() ?? "The request body is not valid.");
        if (settings.Validate() is { } error)
            return InvalidSettings(error);

        await store.SaveAsync(settings, ct);
        return Ok(settings);
    }

    private IActionResult InvalidSettings(string detail) =>
        ProblemTypes.Problem(StatusCodes.Status400BadRequest, ProblemTypes.ValidationFailed, detail: detail);

    private string? FirstModelError() => ModelState.Values
        .SelectMany(v => v.Errors)
        .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage)
        .FirstOrDefault(m => !string.IsNullOrEmpty(m));
}
