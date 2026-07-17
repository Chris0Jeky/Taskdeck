using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Mcp;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Middleware;

/// <summary>
/// Middleware that authenticates MCP HTTP requests using API keys.
/// Extracts a Bearer token from the Authorization header, hashes it with SHA-256,
/// looks up the hash in the ApiKeys table, and sets the user ID in
/// HttpContext.Items for <see cref="HttpUserContextProvider"/>.
///
/// Only active on the MCP endpoint path (/mcp). REST API endpoints continue
/// to use JWT authentication.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    /// <summary>
    /// Authentication type stamped on the ClaimsIdentity for a valid API key so downstream
    /// middleware (e.g. TokenValidationMiddleware) can distinguish API-key principals from JWT ones.
    /// </summary>
    public const string AuthenticationType = "ApiKey";

    /// <summary>
    /// Context item containing the validated API key ID for per-key rate-limit partitioning.
    /// The opaque database ID is used instead of the raw key or user-visible prefix.
    /// </summary>
    public const string ApiKeyIdItemKey = "McpApiKeyId";

    /// <summary>
    /// Context item set when API-key authentication rejects the request, BEFORE the 401 body is
    /// written. <see cref="McpAuthenticationRateLimitingMiddleware"/> consumes the pre-auth IP
    /// failure budget on this marker in a finally block, so a client that aborts the connection
    /// mid-response (making the write throw) cannot evade the failure charge — the key
    /// parse/lookup work has already been spent by that point.
    /// </summary>
    public const string AuthenticationFailedItemKey = "McpAuthenticationFailed";

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TaskdeckDbContext dbContext)
    {
        // Only authenticate MCP endpoint requests
        if (!context.Request.Path.StartsWithSegments(McpEndpointMapping.HttpRoute, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Missing Authorization header. Provide a Bearer token with your API key.");
            return;
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Invalid Authorization header format. Use: Bearer tdsk_...");
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(ApiKey.KeyPrefix))
        {
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Invalid API key format. Keys must start with 'tdsk_'.");
            return;
        }

        // Hash the provided key and look up in the database
        var keyHash = HashKey(token);

        var apiKey = await dbContext.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, context.RequestAborted);

        if (apiKey is null)
        {
            _logger.LogWarning("MCP API key authentication failed: key not found (prefix: {Prefix})",
                token.Length >= 8 ? token[..8] : "short");
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Invalid API key.");
            return;
        }

        if (!apiKey.IsActive)
        {
            var reason = apiKey.RevokedAt is not null ? "revoked" : "expired";
            _logger.LogWarning("MCP API key authentication failed: key is {Reason} (id: {KeyId})",
                reason, apiKey.Id);
            // Return generic message to avoid leaking key state (revoked vs expired)
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Invalid API key.");
            return;
        }

        // Enforce the per-key request budget (McpPerApiKey) at the EARLIEST point the key identity is
        // known — right after the key row is resolved and confirmed active, and BEFORE the user-account
        // lookup and the last-used write below (#1384). The opaque key ID is the budget partition, so
        // store it first. This is the SINGLE charge point for the per-key budget: the /mcp endpoint no
        // longer carries an endpoint-stage McpPerApiKey policy, so a request passing this check is never
        // charged again downstream. A valid-but-over-quota key is rejected with 429 here without setting
        // AuthenticationFailedItemKey and without a 401, so the pre-auth IP FAILURE budget is not spent
        // — valid keys never touch that budget (#1368/#1381), only failed authentications do.
        //
        // Resolved optionally: the limiter is registered only when rate limiting is enabled, so when it
        // is absent the check is skipped (no MCP throttling when disabled), matching prior behaviour.
        context.Items[ApiKeyIdItemKey] = apiKey.Id;

        var perKeyLimiter = context.RequestServices.GetService<McpPerApiKeyRateLimiter>();
        if (perKeyLimiter is not null)
        {
            using var perKeyLease = perKeyLimiter.AttemptAcquire(context);
            if (!perKeyLease.IsAcquired)
            {
                _logger.LogDebug("MCP per-key rate limit exceeded for key {KeyId}", apiKey.Id);
                await McpPerApiKeyRateLimiter.WriteRejectedAsync(context, perKeyLease, context.RequestAborted);
                return;
            }
        }

        // Verify the user account is active
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == apiKey.UserId, context.RequestAborted);

        if (user is null || !user.IsActive)
        {
            _logger.LogWarning("MCP API key authentication failed: user inactive (userId: {UserId})", apiKey.UserId);
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "User account is inactive or has been deleted.");
            return;
        }

        // Set the authenticated user ID for HttpUserContextProvider. The API key ID item was set
        // above, before the per-key budget check.
        context.Items[HttpUserContextProvider.UserIdItemKey] = apiKey.UserId;

        // Establish an authenticated principal so the global authorization FallbackPolicy
        // (RequireAuthenticatedUser, #1132 AC4) is satisfied for valid-key MCP requests — a valid
        // API key IS authentication. It carries only the user id (no JWT 'iat', so
        // TokenValidationMiddleware's JWT-invalidation check is a no-op for it, and that middleware
        // short-circuits on this AuthenticationType). Set before UseAuthentication runs: JwtBearer
        // reads the "Bearer tdsk_..." header and fails to validate it as a JWT, returning a null
        // Principal that AuthenticationMiddleware does not assign over context.User, so this survives.
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, apiKey.UserId.ToString()) },
            authenticationType: AuthenticationType));

        // Update last-used timestamp before continuing the pipeline.
        // This is non-critical so failures are swallowed.
        await UpdateLastUsedAsync(dbContext, apiKey.Id);

        await _next(context);
    }

    private static string HashKey(string plaintextKey)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintextKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task UpdateLastUsedAsync(TaskdeckDbContext dbContext, Guid keyId)
    {
        try
        {
            // Direct update to avoid concurrency issues with the read-only query above.
            await dbContext.ApiKeys
                .Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(k => k.LastUsedAt, DateTimeOffset.UtcNow)
                    .SetProperty(k => k.UpdatedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            // Non-critical: if usage tracking fails, authentication still succeeds.
            _logger.LogDebug(ex, "Failed to update API key last-used timestamp for key {KeyId}", keyId);
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, int statusCode, string message)
    {
        // Mark the authentication failure BEFORE any response write: if the client aborts while
        // the 401 body is being written, WriteAsJsonAsync throws and unwinds past the pre-auth
        // failure-budget middleware — the marker guarantees its finally block still counts the
        // failed attempt (the lookup cost was already paid).
        context.Items[AuthenticationFailedItemKey] = true;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
            ErrorCodes.Unauthorized,
            message));
    }
}
