using System.Security.Claims;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Middleware;

/// <summary>
/// Middleware that rejects HTTP requests from users whose accounts have been deactivated or deleted.
/// Runs after JWT authentication so that the claims principal is already populated.
/// Anonymous (unauthenticated) requests pass through untouched — the downstream
/// [Authorize] attribute handles those.
///
/// This closes the gap where a valid JWT issued before account deletion would remain
/// accepted on subsequent HTTP requests until natural token expiry.
/// It does not invalidate already-established long-lived connections (for example,
/// WebSocket/SignalR connections authenticated before deactivation); those require
/// hub-level checks and/or an explicit server-driven disconnect mechanism if needed.
/// </summary>
public sealed class ActiveUserValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ActiveUserValidationMiddleware> _logger;

    public ActiveUserValidationMiddleware(
        RequestDelegate next,
        ILogger<ActiveUserValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IActiveUserCache cache)
    {
        // Skip if the request is not authenticated (anonymous endpoints, pre-auth routes)
        var identity = context.User.Identity;
        if (identity is null || !identity.IsAuthenticated)
        {
            await _next(context);
            return;
        }

        // Extract user ID from claims
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? context.User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            // No parseable user ID — let downstream authorization handle it
            await _next(context);
            return;
        }

        // Check cache first
        var cachedStatus = cache.GetCachedActiveStatus(userId);
        if (cachedStatus.HasValue)
        {
            if (!cachedStatus.Value)
            {
                await WriteInactiveUserResponse(context);
                return;
            }

            // Active user — continue
            await _next(context);
            return;
        }

        // Cache miss — resolve IUnitOfWork lazily to avoid scoped-service instantiation
        // on every request (cache hits and anonymous requests skip this entirely).
        var unitOfWork = context.RequestServices.GetRequiredService<IUnitOfWork>();
        var user = await unitOfWork.Users.GetByIdAsync(userId, context.RequestAborted);
        if (user is null || !user.IsActive)
        {
            // Cache the inactive/missing status so subsequent requests don't hit DB
            cache.SetActiveStatus(userId, false);

            // Log only the validated Guid (not raw request values) to avoid log-forging.
            _logger.LogInformation(
                "Rejected request from inactive/deleted user {UserId}",
                userId);

            await WriteInactiveUserResponse(context);
            return;
        }

        // User is active — cache and continue
        cache.SetActiveStatus(userId, true);
        await _next(context);
    }

    private static async Task WriteInactiveUserResponse(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
            ErrorCodes.Unauthorized,
            "Your account has been deactivated. Please contact support if you believe this is an error."));
    }
}
