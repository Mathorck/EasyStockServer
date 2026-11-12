using Microsoft.AspNetCore.Diagnostics;

namespace EasyStock.Server.Configuration;

/// <summary>
/// Writes a <see cref="ProblemException"/> as Problem Details, and any other exception as a plain 500
/// <c>internal-error</c> (the exception itself is logged by the exception handler middleware, never sent).
/// </summary>
public sealed class ProblemExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        if (ctx.Response.HasStarted) return false;
        IResult result;
        if (exception is ProblemException p)
        {
            var extensions = new Dictionary<string, object?>(p.Extensions);
            if (p.RetryAfter is { } wait)
            {
                var seconds = ProblemException.Seconds(wait);
                ctx.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                extensions["retryAfter"] = seconds;
            }
            result = Results.Problem(
                type: p.Type,
                title: ProblemTypes.DefaultTitle(p.Type),
                detail: p.Detail,
                statusCode: p.Status,
                extensions: extensions);
        }
        else
        {
            result = Results.Problem(
                type: ProblemTypes.InternalError,
                title: ProblemTypes.DefaultTitle(ProblemTypes.InternalError),
                statusCode: StatusCodes.Status500InternalServerError);
        }
        await result.ExecuteAsync(ctx);
        return true;
    }
}
