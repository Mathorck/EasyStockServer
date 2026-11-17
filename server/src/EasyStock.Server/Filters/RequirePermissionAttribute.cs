using EasyStock.Server.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

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
            context.Result = ProblemTypesHelper.Problem(StatusCodes.Status403Forbidden, ProblemTypesHelper.NotAllowed);
        return Task.CompletedTask;
    }
}
