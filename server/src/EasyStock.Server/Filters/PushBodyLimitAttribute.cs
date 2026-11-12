using EasyStock.Server.Configuration;
using EasyStock.Server.Handlers.Sync;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Server.Filters;

/// <summary>
/// 413 batch-too-large for a body over <see cref="PushHandler.MaxBodyBytes"/>, before model binding reads it.
/// Runs after authorization, so a caller without the permission still gets 401/403.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PushBodyLimitAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Request.ContentLength > PushHandler.MaxBodyBytes)
            context.Result = ProblemTypes.Problem(StatusCodes.Status413PayloadTooLarge, ProblemTypes.BatchTooLarge,
                detail: $"At most {PushHandler.MaxBodyBytes} bytes per batch.");
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
