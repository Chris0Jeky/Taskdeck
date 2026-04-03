using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Middleware;

/// <summary>
/// Middleware that rejects authenticated requests when the user account is
/// inactive or when the JWT was issued before the user's token invalidation
/// timestamp. This ensures that tokens are immediately rejected after account
/// deletion, deactivation, or explicit token invalidation.
///
/// Placed after UseAuthentication() and before UseAuthorization() so that
/// the JWT has already been validated (signature, expiry, issuer, audience)
/// before this middleware runs.
/// </summary>
public sealed class TokenValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenValidationMiddleware> _logger;

    public TokenValidationMiddleware(
        RequestDelegate next,
        ILogger<TokenValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUnitOfWork unitOfWork)
    {
        var user = context.User;

        // Only check authenticated requests — unauthenticated requests pass through
        // to be handled by [Authorize] or anonymous endpoints as normal.
        if (user.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)
                          ?? user.FindFirst(JwtRegisteredClaimNames.Sub);

        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            // Malformed token — let downstream handle it (will fail at controller level)
            await _next(context);
            return;
        }

        // Resolve the user from the database to check active status and token validity.
        // For a local-first SQLite app this is an acceptable per-request cost.
        // IUnitOfWork is injected by the framework from the request scope, so we share
        // the same DbContext/change-tracker as the rest of the pipeline.
        var dbUser = await unitOfWork.Users.GetByIdAsync(userId, context.RequestAborted);

        if (dbUser == null || !dbUser.IsActive)
        {
            _logger.LogInformation(
                "Rejecting request for user {UserId}: account not found or inactive",
                userId);
            await WriteUnauthorizedResponse(context, "User account is inactive or has been deleted.");
            return;
        }

        // Check if the token was issued before the invalidation timestamp.
        if (dbUser.TokenInvalidatedAt.HasValue)
        {
            var tokenIssuedAt = GetTokenIssuedAt(user);

            if (tokenIssuedAt.HasValue && tokenIssuedAt.Value < dbUser.TokenInvalidatedAt.Value)
            {
                _logger.LogInformation(
                    "Rejecting request for user {UserId}: token issued at {IssuedAt} before invalidation at {InvalidatedAt}",
                    userId, tokenIssuedAt.Value, dbUser.TokenInvalidatedAt.Value);
                await WriteUnauthorizedResponse(context, "Token has been invalidated. Please sign in again.");
                return;
            }
        }

        await _next(context);
    }

    private static DateTimeOffset? GetTokenIssuedAt(ClaimsPrincipal principal)
    {
        var iatClaim = principal.FindFirst(JwtRegisteredClaimNames.Iat)
                       ?? principal.FindFirst("iat");

        if (iatClaim == null)
            return null;

        if (long.TryParse(iatClaim.Value, out var unixSeconds))
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        return null;
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
            ErrorCodes.Unauthorized,
            message));
    }
}
