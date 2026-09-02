namespace Taskdeck.Api.Middleware;

/// <summary>
/// Makes every REST API response non-storable independently of optional
/// security-header settings. Registering before error/auth middleware ensures
/// their short-circuit responses carry the same privacy boundary.
/// </summary>
public sealed class ApiCacheControlMiddleware
{
    private const string ApiCacheControl = "no-store, private";
    private readonly RequestDelegate _next;

    public ApiCacheControlMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
        {
            context.Response.Headers.CacheControl = ApiCacheControl;
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
                response.Headers.CacheControl = ApiCacheControl;
                return Task.CompletedTask;
            }, context.Response);
        }

        return _next(context);
    }
}
