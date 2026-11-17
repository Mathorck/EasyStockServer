using EasyStock.Server.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Server.Filters;

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
