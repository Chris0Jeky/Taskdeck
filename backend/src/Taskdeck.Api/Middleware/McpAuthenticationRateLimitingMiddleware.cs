using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;

namespace Taskdeck.Api.Middleware;

/// <summary>
/// Applies the client-address MCP authentication FAILURE budget around API-key parsing and the
/// database lookup. The budget is spent only on requests that fail authentication (401): an
/// exhausted address is rejected before any key lookup, but valid requests pass through without
/// consuming, so multiple keys behind one egress address keep their independent per-key budgets.
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

        // Fast pre-check: reject before any API-key parse or database lookup when this client
        // address has already spent its failure budget for the window. This inspection does not
        // consume a permit, so valid callers behind the same address are never charged here.
        //
        // Residual tradeoff (intentional): the pre-check runs before the auth outcome is known, so a
        // valid key sharing a hostile NAT/proxy that already drained the budget still pays this 429
        // until the window resets. That is the accepted cost of shielding the key lookup from a
        // brute-force flood — narrowing the reject to only-invalid keys would require the lookup we
        // are protecting. Set ForwardedHeaders (behind a trusted proxy) to give each real client its
        // own bucket instead of one shared egress bucket.
        if (limiter.IsFailureBudgetExhausted(context))
        {
            await limiter.WriteFailureBudgetRejectedAsync(context, context.RequestAborted);
            return;
        }

        // Let the request proceed through authentication WITHOUT consuming budget.
        await _next(context);

        // Spend exactly one permit only when authentication failed. ApiKeyMiddleware is the sole
        // source of 401 on /mcp (a valid key sets an authenticated principal and the pipeline then
        // returns non-401), so a 401 unambiguously marks a failed attempt. Successful requests
        // leave the failure budget untouched — the per-key limiter is the only thing that throttles
        // them — which is what restores per-key isolation for any number of keys behind one address.
        if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            limiter.RecordFailedAttempt(context);
        }
    }
}
