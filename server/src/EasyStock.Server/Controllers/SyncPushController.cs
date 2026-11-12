using System.Text.Json.Nodes;
using EasyStock.Server.Filters;
using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Helpers;
using EasyStock.Server.Handlers.Sync;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

[ApiController]
[Route("api/v1/sync/push")]
public sealed class SyncPushController(PushHandler processor) : ControllerBase
{
    /// <summary>
    /// Applies a batch of changed rows, grouped by table, and answers one result per row. The same batch ID
    /// sent again by the same user gets the same answer and changes nothing.
    /// </summary>
    [HttpPost]
    [RequirePermission(Permission.PushChanges)]
    [PushBodyLimit] // 413 batch-too-large when Content-Length says so
    [RequestSizeLimit(PushHandler.MaxBodyBytes)] // the backstop for a body without Content-Length
    [ProducesResponseType<SyncPushResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public async Task<IActionResult> Push(SyncPushRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.BatchId, out var batchId) || request.Rows is null)
            return ProblemTypes.Problem(StatusCodes.Status400BadRequest, ProblemTypes.ValidationFailed,
                detail: "batchId must be a UUID and rows an object of row lists by table.");
        if (request.Rows.Values.Sum(rows => rows?.Count ?? 0) > PushHandler.MaxRows)
            return ProblemTypes.Problem(StatusCodes.Status413PayloadTooLarge, ProblemTypes.BatchTooLarge,
                detail: $"At most {PushHandler.MaxRows} rows per batch.");

        var rows = request.Rows.ToDictionary(t => t.Key, t => t.Value ?? new List<JsonNode?>());
        var outcome = await processor.PushAsync(batchId, rows, ct);
        if (outcome.BatchIdReused)
            return ProblemTypes.Problem(StatusCodes.Status409Conflict, ProblemTypes.BatchIdReused);
        // The saved bytes, so a replayed batch gets exactly the first answer.
        return Content(outcome.Json!, "application/json");
    }
}
