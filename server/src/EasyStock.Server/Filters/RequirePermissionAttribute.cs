using EasyStock.Server.Configuration;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Filters;

/// <summary>
/// Requires an authenticated caller whose role (from the database, through <see cref="ICurrentUser"/>) has
/// <paramref name="permission"/>. Answers 401 (<c>unauthorized</c>, or the reason the token was refused:
/// <c>token-expired</c>, <c>session-revoked</c>, <c>account-disabled</c>) or 403 <c>not-allowed</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(Permission permission) : Attribute, IAsyncAuthorizationFilter
{
    public Permission Permission { get; } = permission;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var current = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!current.IsAuthenticated)
            context.Result = AuthFailureHelper.Unauthenticated(context.HttpContext);
        else if (!Permissions.Can(current.Role, Permission))
            context.Result = ProblemTypes.Problem(StatusCodes.Status403Forbidden, ProblemTypes.NotAllowed);
        return Task.CompletedTask;
    }
}

/// <summary>Requires an authenticated caller, whatever the role (for /me, logout, complete-promotion).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSessionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var current = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!current.IsAuthenticated)
            context.Result = AuthFailureHelper.Unauthenticated(context.HttpContext);
        return Task.CompletedTask;
    }
}
