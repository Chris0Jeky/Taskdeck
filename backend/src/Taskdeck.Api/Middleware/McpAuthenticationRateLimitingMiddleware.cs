using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;

namespace Taskdeck.Api.Middleware;

/// <summary>
/// Applies the aggregate client-address MCP authentication limit before API-key parsing
/// and database lookup. Valid requests continue to the separate per-key endpoint policy.
/// </summary>
public sealed class McpAuthenticationRateLimitingMiddleware
{
    private readonly RequestDelegate _next;

    public McpAuthenticationRateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        McpAuthenticationAttemptLimiter limiter)
    {
        if (!context.Request.Path.StartsWithSegments(
                McpEndpointMapping.HttpRoute,
                StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        using var lease = await limiter.AcquireAsync(context);
        if (!lease.IsAcquired)
        {
            await McpAuthenticationAttemptLimiter.WriteRejectedAsync(
                context,
                lease,
                context.RequestAborted);
            return;
        }

        await _next(context);
    }
}
