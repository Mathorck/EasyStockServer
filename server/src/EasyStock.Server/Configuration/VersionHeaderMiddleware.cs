namespace EasyStock.Server.Configuration;

/// <summary>Rejects requests without acceptable X-EasyStock-App-Version and X-EasyStock-Schema-Version headers.</summary>
public sealed class VersionHeaderMiddleware(RequestDelegate next)
{
    public const string AppHeader = "X-EasyStock-App-Version";
    public const string SchemaHeader = "X-EasyStock-Schema-Version";

    private static readonly Version MinApp = Version.Parse(Compat.MinAppVersion);

    public Task InvokeAsync(HttpContext ctx) => IsExempt(ctx.Request) || IsAcceptable(ctx.Request) ? next(ctx) : Reject(ctx);

    private static bool IsExempt(HttpRequest r)
    {
        var path = r.Path;
        if (path.Equals("/health") || path.StartsWithSegments("/openapi")) return true;
        if (HttpMethods.IsGet(r.Method) && path.Equals("/api/v1/server-info")) return true;
        return (HttpMethods.IsGet(r.Method) || HttpMethods.IsHead(r.Method)) && path.StartsWithSegments("/api/v1/images");
    }

    private static bool IsAcceptable(HttpRequest r)
    {
        var app = r.Headers[AppHeader].ToString();
        var schema = r.Headers[SchemaHeader].ToString();
        // Ignore any pre-release or build suffix such as "1.2.3-beta".
        var core = app.Split('-', '+')[0];
        return Version.TryParse(core, out var v) && v >= MinApp
            && int.TryParse(schema, out var s) && Compat.SupportedSchemaVersions.Contains(s);
    }

    private static Task Reject(HttpContext ctx) =>
        Results.Problem(
            type: ProblemTypes.AppTooOld,
            title: "App version not supported",
            detail: "Update the EasyStock app.",
            statusCode: StatusCodes.Status426UpgradeRequired).ExecuteAsync(ctx);
}
