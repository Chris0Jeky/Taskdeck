using System.Globalization;
using System.Threading.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.RateLimiting;

/// <summary>
/// Bounds the cost of MCP authentication FAILURES by trusted client-address metadata before an
/// API key has been validated. The budget is spent only when authentication fails, so any number
/// of valid keys behind one egress address (NAT, proxy, VPN, shared CI runner) keep their
/// independent per-key budgets (the #1364 isolation contract) instead of sharing this bucket.
/// </summary>
public sealed class McpAuthenticationAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;
    private readonly int _windowSeconds;

    public McpAuthenticationAttemptLimiter(RateLimitPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var permitLimit = Math.Max(settings.PermitLimit, 1);
        _windowSeconds = Math.Max(settings.WindowSeconds, 1);
        var window = TimeSpan.FromSeconds(_windowSeconds);
        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var clientAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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
    }

    /// <summary>
    /// Seconds a rejected client should wait before retrying. Exposed for the pre-check reject,
    /// which inspects availability rather than acquiring a lease and therefore has no
    /// <see cref="MetadataName.RetryAfter"/> metadata to read.
    /// </summary>
    public int WindowSeconds => _windowSeconds;

    /// <summary>
    /// Non-consuming inspection: <c>true</c> when the client address has already spent its failure
    /// budget for the current window. Used as a pre-authentication fast reject so an exhausted
    /// address never reaches the API-key parse/database lookup — preserving the brute-force and
    /// lookup-cost protection while never charging valid traffic.
    /// <para>
    /// A small TOCTOU exists between this check and <see cref="RecordFailedAttempt"/> (and across
    /// concurrent requests): a burst can momentarily overshoot the permit limit by the number of
    /// in-flight requests. That bounded overshoot is acceptable for a DoS-protection budget.
    /// </para>
    /// </summary>
    public bool IsFailureBudgetExhausted(HttpContext context)
    {
        // A partition that has never been touched reports its full permit count (or null before
        // creation); either way it is not exhausted, so a first attempt from an address proceeds.
        var statistics = _limiter.GetStatistics(context);
        return statistics is not null && statistics.CurrentAvailablePermits <= 0;
    }

    /// <summary>
    /// Consumes exactly one permit from the client address's failure budget. Called only after an
    /// authentication failure (401), so successful authentications never spend IP budget. The
    /// acquired lease is discarded intentionally: fixed-window limiters do not refund on dispose,
    /// which is precisely the "spend on failure" semantics we want. Acquisition may fail when a
    /// concurrent burst raced past the pre-check and drained the window first; that is the accepted
    /// bounded overshoot and needs no special handling (the response is already written).
    /// </summary>
    public void RecordFailedAttempt(HttpContext context)
    {
        using var lease = _limiter.AttemptAcquire(context, permitCount: 1);
    }

    /// <summary>
    /// Writes the pre-check rejection using the same 429 contract as the endpoint per-key limiter:
    /// <c>application/json</c> <see cref="ApiErrorResponse"/>, <c>Retry-After</c>, and the policy
    /// header. Retry-After is the full window length (an upper bound on the wait, since the exact
    /// replenishment instant is not exposed by the availability inspection).
    /// </summary>
    public async Task WriteFailureBudgetRejectedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = _windowSeconds.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Policy"] = RateLimitingPolicyNames.McpAuthenticationPerIp;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                ErrorCodes.TooManyRequests,
                $"Rate limit exceeded. Retry after {_windowSeconds} seconds."),
            cancellationToken);
    }

    public void Dispose() => _limiter.Dispose();
}
