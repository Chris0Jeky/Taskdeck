using System.Globalization;
using System.Threading.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.RateLimiting;

/// <summary>
/// Bounds MCP authentication work by trusted client-address metadata before an API key
/// has been validated and the endpoint's per-key limiter can select its partition.
/// </summary>
public sealed class McpAuthenticationAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    public McpAuthenticationAttemptLimiter(RateLimitPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var permitLimit = Math.Max(settings.PermitLimit, 1);
        var window = TimeSpan.FromSeconds(Math.Max(settings.WindowSeconds, 1));
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

    public ValueTask<RateLimitLease> AcquireAsync(HttpContext context) =>
        _limiter.AcquireAsync(context, permitCount: 1, context.RequestAborted);

    public static async Task WriteRejectedAsync(
        HttpContext context,
        RateLimitLease lease,
        CancellationToken cancellationToken)
    {
        var retryAfterSeconds = 1;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = Math.Max((int)Math.Ceiling(retryAfter.TotalSeconds), 1);
        }

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

    public void Dispose() => _limiter.Dispose();
}
