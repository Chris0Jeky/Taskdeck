using System.Globalization;
using System.Threading.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.RateLimiting;

/// <summary>
/// Bounds the cost of MCP authentication FAILURES by trusted client-address metadata before an
/// API key has been validated, with two cooperating per-address limiters:
/// <list type="bullet">
/// <item><description><b>Failure window</b> (fixed window, <c>McpAuthenticationPerIp</c>): a permit
/// is spent only when authentication fails (401), so any number of valid keys behind one egress
/// address (NAT, proxy, VPN, shared CI runner) keep their independent per-key budgets (the #1364
/// isolation contract) instead of sharing this bucket.</description></item>
/// <item><description><b>Concurrency gate</b> (<c>McpAuthenticationPerIpConcurrency</c>): admission
/// control the failure window alone cannot provide, because its consumption happens after the
/// response is known. The gate caps how many /mcp requests from one address may be in flight past
/// the pre-check simultaneously; leases are released when each request completes (concurrency
/// leases are releasable, unlike window permits).</description></item>
/// </list>
/// Combined invariant: at any instant, in-flight pre-auth work per address is at most the
/// concurrency cap; once the failure window is spent, the pre-check rejects everything — so
/// failed-authentication key lookups per address per window are bounded by
/// <c>PermitLimit + concurrency cap</c> (the cap-many requests that may already be in flight when
/// the window closes).
/// </summary>
public sealed class McpAuthenticationAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _failureLimiter;
    private readonly PartitionedRateLimiter<HttpContext> _concurrencyLimiter;
    private readonly int _windowSeconds;

    public McpAuthenticationAttemptLimiter(RateLimitPolicySettings settings, int concurrencyLimit)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var permitLimit = Math.Max(settings.PermitLimit, 1);
        _windowSeconds = Math.Max(settings.WindowSeconds, 1);
        var window = TimeSpan.FromSeconds(_windowSeconds);
        _failureLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var clientAddress = ResolveClientAddress(context);
            return RateLimitPartition.GetFixedWindowLimiter(
                $"mcp-auth-ip:{clientAddress}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });

        var concurrencyPermits = Math.Max(concurrencyLimit, 1);
        _concurrencyLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var clientAddress = ResolveClientAddress(context);
            return RateLimitPartition.GetConcurrencyLimiter(
                $"mcp-auth-ip-concurrency:{clientAddress}",
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = concurrencyPermits,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    // No queueing: an over-cap request is rejected immediately rather than parked,
                    // so a flood cannot hold server resources hostage waiting for slots.
                    QueueLimit = 0
                });
        });
    }

    /// <summary>
    /// Non-consuming inspection: <c>true</c> when the client address has already spent its failure
    /// budget for the current window. Used as a pre-authentication fast reject so an exhausted
    /// address never reaches the API-key parse/database lookup — preserving the brute-force and
    /// lookup-cost protection while never charging valid traffic.
    /// <para>
    /// This check alone is not admission control: consumption happens after the response (401), so
    /// concurrent requests can all pass while permits remain. The overshoot is bounded by the
    /// concurrency gate (<see cref="TryAcquirePreAuthSlot"/>): at most cap-many requests per
    /// address are past the pre-check at once, so failed-lookup work per window is at most
    /// <c>PermitLimit + concurrency cap</c>.
    /// </para>
    /// </summary>
    public bool IsFailureBudgetExhausted(HttpContext context)
    {
        // GetStatistics lazily creates the partition when needed and reports its full permit count
        // for a first-time address, so a never-seen address is never treated as exhausted. The null
        // branch is purely defensive (the API is typed nullable); it is not an expected state.
        var statistics = _failureLimiter.GetStatistics(context);
        return statistics is not null && statistics.CurrentAvailablePermits <= 0;
    }

    /// <summary>
    /// Admission control: attempts to take one of the address's concurrent pre-auth slots. The
    /// returned lease MUST be disposed when the request completes — disposal releases the slot
    /// (concurrency leases, unlike window permits, are releasable). When <c>IsAcquired</c> is
    /// <c>false</c> the address already has the maximum number of /mcp requests in flight and the
    /// caller must reject immediately. Long-lived requests (e.g. streaming) hold their slot for
    /// their full duration — that is the point of the bound, and the documented tradeoff for
    /// clients multiplexing many concurrent requests through one address.
    /// </summary>
    public RateLimitLease TryAcquirePreAuthSlot(HttpContext context) =>
        _concurrencyLimiter.AttemptAcquire(context, permitCount: 1);

    /// <summary>
    /// Consumes exactly one permit from the client address's failure budget. Called only after an
    /// authentication failure (401), so successful authentications never spend IP budget.
    /// Acquisition may fail when concurrent in-flight failures (bounded by the concurrency cap)
    /// drained the window first; that is the accepted bounded overshoot and needs no special
    /// handling (the response is already written).
    /// </summary>
    public void RecordFailedAttempt(HttpContext context)
    {
        // Acquire-and-dispose in one expression: the lease is only needed to spend the permit, and a
        // fixed-window limiter does not refund on dispose, so disposing immediately is the intended
        // "spend on failure" effect (AttemptAcquire never returns null).
        _failureLimiter.AttemptAcquire(context, permitCount: 1).Dispose();
    }

    /// <summary>
    /// Writes the failure-budget pre-check rejection using the same 429 contract as the endpoint
    /// per-key limiter: <c>application/json</c> <see cref="ApiErrorResponse"/>, <c>Retry-After</c>,
    /// and the policy header. <c>Retry-After</c> is deliberately the FULL window length — a safe
    /// over-estimate, since the non-consuming availability inspection does not expose the exact
    /// replenishment instant (the old acquire-on-entry design read it from lease metadata).
    /// </summary>
    public Task WriteFailureBudgetRejectedAsync(HttpContext context, CancellationToken cancellationToken) =>
        WriteRejectedAsync(context, _windowSeconds, cancellationToken);

    /// <summary>
    /// Writes the concurrency-gate rejection with the same 429 contract. <c>Retry-After</c> is one
    /// second: a slot frees as soon as any in-flight request from the address completes, so the
    /// shortest meaningful retry hint applies rather than the failure window.
    /// </summary>
    public Task WriteConcurrencyRejectedAsync(HttpContext context, CancellationToken cancellationToken) =>
        WriteRejectedAsync(context, retryAfterSeconds: 1, cancellationToken);

    private static async Task WriteRejectedAsync(
        HttpContext context,
        int retryAfterSeconds,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Policy"] = RateLimitingPolicyNames.McpAuthenticationPerIp;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                ErrorCodes.TooManyRequests,
                $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds."),
            cancellationToken);
    }

    private static string ResolveClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public void Dispose()
    {
        _failureLimiter.Dispose();
        _concurrencyLimiter.Dispose();
    }
}
