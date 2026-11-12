using EasyStock.Server.Enums;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Helpers;

/// <summary>Why a Bearer token was refused, kept for the 401 answer.</summary>
public static class AuthFailureHelper
{
    private const string Key = "EasyStock.AuthFailure";

    public static void Record(HttpContext ctx, string problemType) => ctx.Items[Key] = problemType;

    public static string TypeFor(HttpContext ctx) => ctx.Items[Key] as string ?? ProblemTypesHelper.Unauthorized;

    public static ObjectResult Unauthenticated(HttpContext ctx)
    {
        ctx.Response.Headers.WWWAuthenticate = "Bearer";
        return ProblemTypesHelper.Problem(StatusCodes.Status401Unauthorized, TypeFor(ctx));
    }
}
