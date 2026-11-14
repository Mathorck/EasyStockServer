using EasyStock.Server.Helpers;

namespace EasyStock.Server.Configuration;

/// <summary>Rejects requests without acceptable X-EasyStock-App-Version and X-EasyStock-Schema-Version headers.</summary>
public sealed class VersionHeaderMiddleware(RequestDelegate next)
{
    public const string AppHeader = "X-EasyStock-App-Version";
    public const string SchemaHeader = "X-EasyStock-Schema-Version";

    private static readonly Version MinApp = ParseVersion(CompatHelper.MinAppVersion)
        ?? throw new InvalidOperationException($"CompatHelper.MinAppVersion \"{CompatHelper.MinAppVersion}\" is not a valid version (expected major[.minor[.patch]] with an optional -suffix).");

    public Task InvokeAsync(HttpContext ctx) => IsExempt(ctx.Request.Method, ctx.Request.Path) || IsAcceptable(ctx.Request) ? next(ctx) : Reject(ctx);

    /// <summary>Routes that need no version headers. Shared with the OpenAPI document, so it documents the same rule.</summary>
    public static bool IsExempt(string method, PathString path)
    {
        if (path.Equals("/health") || path.StartsWithSegments("/openapi")) return true;
        if (HttpMethods.IsGet(method) && path.Equals("/api/v1/server-info")) return true;
        return (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) && path.StartsWithSegments("/api/v1/images");
    }

    private static bool IsAcceptable(HttpRequest r)
    {
        var app = r.Headers[AppHeader].ToString();
        var schema = r.Headers[SchemaHeader].ToString();
        return ParseVersion(app) is { } v && v >= MinApp
            && int.TryParse(schema, out var s) && CompatHelper.SupportedSchemaVersions.Contains(s);
    }

    /// <summary>Parses "1", "1.2" or "1.2.3", ignoring a pre-release or build suffix ("1.2.3-beta"). Missing parts are 0, so "1.0" equals "1.0.0".</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('-', '+')[0].Split('.');
        if (parts.Length is < 1 or > 3) return null;
        var n = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n[i])) return null;
        return new Version(n[0], n[1], n[2]);
    }

    private static Task Reject(HttpContext ctx) =>
        Results.Problem(
            type: ProblemTypesHelper.AppTooOld,
            title: "App version not supported",
            detail: "Update the EasyStock app.",
            statusCode: StatusCodes.Status426UpgradeRequired,
            extensions: new Dictionary<string, object?>
            {
                ["minAppVersion"] = CompatHelper.MinAppVersion,
                ["supportedSchemaVersions"] = CompatHelper.SupportedSchemaVersions,
            }).ExecuteAsync(ctx);
}
