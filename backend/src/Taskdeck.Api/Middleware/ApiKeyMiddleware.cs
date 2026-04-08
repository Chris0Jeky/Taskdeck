using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Api.Contracts;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Mcp;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Middleware;

/// <summary>
/// Middleware that authenticates MCP HTTP requests using API keys.
/// Extracts a Bearer token from the Authorization header, validates it against
/// the ApiKeys table using constant-time hash comparison, and sets the user ID
/// in HttpContext.Items for <see cref="HttpUserContextProvider"/>.
///
/// Only active on the MCP endpoint path (/mcp). REST API endpoints continue
/// to use JWT authentication.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    /// <summary>The path prefix that triggers API key authentication.</summary>
    private const string McpPathPrefix = "/mcp";

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TaskdeckDbContext dbContext)
    {
        // Only authenticate MCP endpoint requests
        if (!context.Request.Path.StartsWithSegments(McpPathPrefix, StringComparison.OrdinalIgnoreCase))
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
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                $"API key is {reason}.");
            return;
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

        // Set the authenticated user ID for HttpUserContextProvider
        context.Items[HttpUserContextProvider.UserIdItemKey] = apiKey.UserId;

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

    private static async Task UpdateLastUsedAsync(TaskdeckDbContext dbContext, Guid keyId)
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
        catch
        {
            // Non-critical: if usage tracking fails, authentication still succeeds.
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
            ErrorCodes.Unauthorized,
            message));
    }
}
