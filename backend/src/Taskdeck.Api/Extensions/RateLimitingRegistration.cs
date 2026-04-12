using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Extensions;

public static class RateLimitingRegistration
{
    public static IServiceCollection AddTaskdeckRateLimiting(
        this IServiceCollection services,
        RateLimitingSettings settings)
    {
        services.AddRateLimiter(options => ConfigureRateLimiting(options, settings));
        return services;
    }

    private static void ConfigureRateLimiting(RateLimiterOptions options, RateLimitingSettings settings)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.HttpContext.Response.HasStarted)
            {
                return;
            }

            var retryAfterSeconds = 1;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                retryAfterSeconds = Math.Max((int)Math.Ceiling(retryAfter.TotalSeconds), 1);
            }

            var policyName = context.HttpContext
                .GetEndpoint()?
                .Metadata
                .GetMetadata<EnableRateLimitingAttribute>()?
                .PolicyName;

            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(policyName))
            {
                context.HttpContext.Response.Headers["X-RateLimit-Policy"] = policyName;
            }

            context.HttpContext.Response.ContentType = "application/json";
            await context.HttpContext.Response.WriteAsJsonAsync(
                new ApiErrorResponse(
                    ErrorCodes.TooManyRequests,
                    $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds."),
                cancellationToken);
        };

        options.AddPolicy(RateLimitingPolicyNames.AuthPerIp, httpContext =>
        {
            var partitionKey = $"auth-ip:{ResolveClientAddress(httpContext)}";
            return BuildFixedWindowPartition(partitionKey, settings.AuthPerIp);
        });

        options.AddPolicy(RateLimitingPolicyNames.HotPathPerUser, httpContext =>
        {
            var partitionKey = $"hot-user:{ResolveUserOrClientIdentifier(httpContext)}";
            return BuildFixedWindowPartition(partitionKey, settings.HotPathPerUser);
        });

        options.AddPolicy(RateLimitingPolicyNames.CaptureWritePerUser, httpContext =>
        {
            var partitionKey = $"capture-user:{ResolveUserOrClientIdentifier(httpContext)}";
            return BuildFixedWindowPartition(partitionKey, settings.CaptureWritePerUser);
        });

        options.AddPolicy(RateLimitingPolicyNames.NoteImportPerUser, httpContext =>
        {
            var partitionKey = $"note-import-user:{ResolveUserOrClientIdentifier(httpContext)}";
            return BuildFixedWindowPartition(partitionKey, settings.NoteImportPerUser);
        });

        options.AddPolicy(RateLimitingPolicyNames.McpPerApiKey, httpContext =>
        {
            // Partition by API key user or fall back to IP for unauthenticated attempts.
            var partitionKey = $"mcp-apikey:{ResolveUserOrClientIdentifier(httpContext)}";
            return BuildFixedWindowPartition(partitionKey, settings.McpPerApiKey);
        });
    }

    private static RateLimitPartition<string> BuildFixedWindowPartition(string partitionKey, RateLimitPolicySettings policy)
    {
        var permitLimit = Math.Max(policy.PermitLimit, 1);
        var windowSeconds = Math.Max(policy.WindowSeconds, 1);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    private static string ResolveUserOrClientIdentifier(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        return !string.IsNullOrWhiteSpace(userId)
            ? userId
            : ResolveClientAddress(context);
    }

    private static string ResolveClientAddress(HttpContext context)
    {
        // Trust only connection metadata here. Raw forwarded headers are caller-controlled unless
        // forwarded-header middleware is explicitly configured with trusted proxies/networks.
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
