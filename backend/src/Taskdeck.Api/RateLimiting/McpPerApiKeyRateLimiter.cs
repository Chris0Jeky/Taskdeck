using System.Globalization;
using System.Threading.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Middleware;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.RateLimiting;

/// <summary>
/// Enforces the MCP per-API-key request budget (the <c>McpPerApiKey</c> policy — default 60/min per
/// key) as a single, early check-and-charge inside <see cref="ApiKeyMiddleware"/>, immediately after
/// the key row is resolved and confirmed active and BEFORE the user-account lookup and the last-used
/// write (#1384).
/// <para>
/// This replaces the former endpoint-stage <c>McpPerApiKey</c> rate-limiting policy, which ran after
/// <see cref="ApiKeyMiddleware"/> completed all of its authentication-stage database work: a valid
/// but over-quota key still paid the SHA-256 hash, ApiKeys lookup, Users lookup, and
/// <c>UpdateLastUsedAsync</c> write on every request before the endpoint limiter returned 429. The
/// budget is now consumed exactly once, at the earliest point the opaque key ID is known, so an
/// over-quota key is rejected before that per-request database work — bounding auth-stage cost by the
/// advertised per-key quota, not just by connection/concurrency limits.
/// </para>
/// <para>
/// The partition key (<c>mcp-apikey:{keyId}</c>) and window settings are identical to the endpoint
/// policy it replaces, and <see cref="WriteRejectedAsync"/> emits the same 429 contract, so the
/// externally observable behaviour is unchanged apart from happening earlier and only once.
/// </para>
/// </summary>
public sealed class McpPerApiKeyRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    public McpPerApiKeyRateLimiter(RateLimitPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var permitLimit = Math.Max(settings.PermitLimit, 1);
        var windowSeconds = Math.Max(settings.WindowSeconds, 1);
        var window = TimeSpan.FromSeconds(windowSeconds);

        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                ResolvePartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    }

    /// <summary>
    /// Charges exactly one permit against the calling key's budget. The returned lease MUST be
    /// disposed: a fixed-window limiter never refunds on dispose, so disposing an acquired lease is
    /// the intended "spend one" effect, and disposing a rejected lease simply releases its metadata.
    /// When <see cref="RateLimitLease.IsAcquired"/> is <c>false</c> the key is over quota for the
    /// window and the caller must reject with <see cref="WriteRejectedAsync"/> before doing any
    /// further authentication-stage work.
    /// </summary>
    public RateLimitLease AttemptAcquire(HttpContext context) =>
        _limiter.AttemptAcquire(context, permitCount: 1);

    /// <summary>
    /// Writes the per-key 429 rejection using the same contract as the endpoint policy it replaces:
    /// <c>429</c>, a <c>Retry-After</c> header derived from the rejected lease's replenishment
    /// metadata (full window when available, minimum one second), the <c>McpPerApiKey</c> policy
    /// header, and an <c>application/json</c> <see cref="ApiErrorResponse"/>. No-ops when the
    /// response has already started, matching the endpoint limiter's <c>OnRejected</c> guard.
    /// </summary>
    public static async Task WriteRejectedAsync(
        HttpContext context,
        RateLimitLease lease,
        CancellationToken cancellationToken)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var retryAfterSeconds = 1;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = Math.Max((int)Math.Ceiling(retryAfter.TotalSeconds), 1);
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Policy"] = RateLimitingPolicyNames.McpPerApiKey;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                ErrorCodes.TooManyRequests,
                $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds."),
            cancellationToken);
    }

    private static string ResolvePartitionKey(HttpContext context)
    {
        // Mirror the former endpoint policy's partition derivation exactly: the validated opaque key
        // ID (set by ApiKeyMiddleware before this runs) is the budget partition. Partitioning by user
        // ID would make independent keys owned by the same user share one budget, contrary to the
        // per-key contract (#1364). The client-address fallback never fires in practice because this
        // limiter is only consulted after the key ID is stored, but it preserves the endpoint
        // policy's defensive default.
        if (context.Items.TryGetValue(ApiKeyMiddleware.ApiKeyIdItemKey, out var apiKeyId)
            && apiKeyId is Guid apiKeyGuid)
        {
            return $"mcp-apikey:{apiKeyGuid}";
        }

        return $"mcp-apikey:{context.Connection?.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    public void Dispose() => _limiter.Dispose();
}
