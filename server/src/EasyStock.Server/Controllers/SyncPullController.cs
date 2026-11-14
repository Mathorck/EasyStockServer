using EasyStock.Server.DB;
using EasyStock.Server.Helpers;
using EasyStock.Server.Filters;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Enums;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Sync;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Controllers;

// No [Produces]: it would rewrite the problem+json content type of the 400 answer.
[ApiController]
[Route("api/v1/sync/pull")]
public sealed class SyncPullController(
    PullHandler builder, DatasetEpochHandler epochs, ICurrentUser user, EasyStockDbContext db) : ControllerBase
{
    /// <summary>
    /// Rows with server_seq greater than <paramref name="since"/>, at most <paramref name="limit"/> (default 500,
    /// maximum 2000), in server_seq order. Continue with since = next while hasMore. Never takes the sync lock.
    /// </summary>
    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    [ProducesResponseType<SyncPullResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Get(
        [FromQuery] long since = 0, [FromQuery] int limit = PullHandler.DefaultLimit,
        [FromHeader(Name = DatasetEpochHandler.HeaderName)] string? datasetEpoch = null,
        CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (since < 0) errors["since"] = ["must be 0 or more"];
        if (limit is < 1 or > PullHandler.MaxLimit) errors["limit"] = [$"must be between 1 and {PullHandler.MaxLimit}"];
        if (errors.Count > 0)
            return new ObjectResult(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Type = ProblemTypesHelper.ValidationFailed,
                Title = "Invalid query",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" },
            };

        // One read for both the check and the answer: checking one value and answering another would let a phone
        // with the old epoch store the new one next to its stale rows.
        var epoch = await epochs.GetAsync(ct);
        if (!DatasetEpochHandler.Accepts(datasetEpoch, epoch))
            return ProblemTypesHelper.DatasetResetProblem(epoch);
        // A Guest just promoted holds a Guest-filtered copy: the same reset, with the unchanged epoch, makes this
        // phone pull everything from 0 (FullPullMarkHelper). Only checked for a full account: a request still
        // authenticated as a Guest neither trips nor clears the mark.
        if (!user.IsGuest)
        {
            if (since > 0 && await FullPullMarkHelper.IsMarkedAsync(db, user.SessionId, ct))
                return ProblemTypesHelper.DatasetResetProblem(epoch);
            if (since == 0) await FullPullMarkHelper.ClearAsync(db, user.SessionId, ct);
        }
        return Ok(await builder.BuildAsync(user, since, limit, epoch, ct));
    }
}
