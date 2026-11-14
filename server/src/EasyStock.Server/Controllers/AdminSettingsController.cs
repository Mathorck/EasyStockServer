using EasyStock.Server.Helpers;
using EasyStock.Server.Filters;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Settings;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

// Not [ApiController]: its automatic 400 uses the RFC 9110 type, but the API promises `validation-failed`.
// The body is checked by hand below, so every invalid request gets the same Problem Details.
[Route("api/v1/admin/settings")]
[ApiExplorerSettings(IgnoreApi = false)] // without [ApiController] it would be left out of the OpenAPI document
public sealed class AdminSettingsController(SettingsHandler store) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.Administer)]
    [ProducesResponseType<ServerSettings>(StatusCodes.Status200OK)]
    public async Task<ServerSettings> Get(CancellationToken ct) => await store.GetAsync(ct);

    [HttpPut]
    [RequirePermission(Permission.Administer)]
    [ProducesResponseType<ServerSettings>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemTypesHelper.ContentType)]
    public async Task<IActionResult> Put([FromBody] ServerSettings? settings, CancellationToken ct)
    {
        if (settings is null)
            return InvalidSettings("The request body must be a JSON object with serverName.");
        if (!ModelState.IsValid)
            return InvalidSettings(FirstModelError() ?? "The request body is not valid.");
        if (settings.Validate() is { } error)
            return InvalidSettings(error);

        await store.SaveAsync(settings, ct);
        return Ok(settings with { MovementAuthorRetentionDays = null });
    }

    private IActionResult InvalidSettings(string detail) =>
        ProblemTypesHelper.Problem(StatusCodes.Status400BadRequest, ProblemTypesHelper.ValidationFailed, detail: detail);

    private string? FirstModelError() => ModelState.Values
        .SelectMany(v => v.Errors)
        .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage)
        .FirstOrDefault(m => !string.IsNullOrEmpty(m));
}
