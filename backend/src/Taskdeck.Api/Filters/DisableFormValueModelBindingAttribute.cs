using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Taskdeck.Api.Filters;

/// <summary>
/// Prevents MVC's form value providers from consuming and buffering a multipart
/// body before a streaming action can apply its own byte limits.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        for (var index = context.ValueProviderFactories.Count - 1; index >= 0; index--)
        {
            if (context.ValueProviderFactories[index] is FormValueProviderFactory or
                FormFileValueProviderFactory or JQueryFormValueProviderFactory)
            {
                context.ValueProviderFactories.RemoveAt(index);
            }
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
