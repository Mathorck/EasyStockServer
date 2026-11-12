using EasyStock.Server.Configuration;
using EasyStock.Server.Handlers.Sync;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Server.Filters;

/// <summary>
/// 409 dataset-reset when <see cref="DatasetEpochHandler.HeaderName"/> is sent and differs from the current epoch.
/// A compare in memory, after authorization and before the body is read. A missing header is accepted.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DatasetEpochCheckAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var header = http.Request.Headers[DatasetEpochHandler.HeaderName].ToString();
        if (header.Length > 0)
        {
            var current = await http.RequestServices.GetRequiredService<DatasetEpochHandler>().GetAsync(http.RequestAborted);
            if (!DatasetEpochHandler.Accepts(header, current))
            {
                context.Result = ProblemTypes.DatasetResetProblem(current);
                return;
            }
        }
        await next();
    }
}
