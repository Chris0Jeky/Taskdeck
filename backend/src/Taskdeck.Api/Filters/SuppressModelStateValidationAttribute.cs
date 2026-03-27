using Microsoft.AspNetCore.Mvc.Filters;

namespace Taskdeck.Api.Filters;

/// <summary>
/// Suppresses the automatic 400 response that <c>[ApiController]</c> triggers
/// when model state is invalid. The action method is still invoked and can
/// inspect <c>ModelState</c> or the bound parameter manually.
/// Applied as an action filter that runs before the built-in
/// <c>ModelStateInvalidFilter</c> (order -2000) and clears any validation errors.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SuppressModelStateValidationAttribute : Attribute, IOrderedFilter, IActionFilter
{
    /// <summary>
    /// Run before the built-in ModelStateInvalidFilter (order = -2000).
    /// </summary>
    public int Order => -2100;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            context.ModelState.Clear();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
