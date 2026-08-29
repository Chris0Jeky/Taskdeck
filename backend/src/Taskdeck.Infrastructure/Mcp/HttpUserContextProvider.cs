using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Infrastructure.Mcp;

/// <summary>
/// Resolves user identity for HTTP MCP sessions.
/// The API key middleware sets the user ID in <see cref="HttpContext.Items"/>
/// after validating the Bearer token. This provider reads that value.
/// </summary>
public class HttpUserContextProvider : IUserContextProvider
{
    /// <summary>Key used in HttpContext.Items to store the authenticated user ID.</summary>
    public const string UserIdItemKey = "McpApiKeyUserId";

    /// <summary>Key used in HttpContext.Items to store the validated API key scope mask.</summary>
    public const string ScopesItemKey = "McpApiKeyScopes";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HttpUserContextProvider> _logger;

    public HttpUserContextProvider(
        IHttpContextAccessor httpContextAccessor,
        ILogger<HttpUserContextProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<McpUserContext> GetCurrentContextAsync(CancellationToken cancellationToken = default)
    {
        var context = GetContextFromHttp();
        if (context is null)
        {
            throw new InvalidOperationException(
                "MCP HTTP: no validated API key context. Ensure the request includes a valid API key.");
        }

        return Task.FromResult(context.Value);
    }

    /// <inheritdoc />
    public Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var context = GetContextFromHttp();
        if (context is null)
        {
            throw new InvalidOperationException(
                "MCP HTTP: no validated API key context. Ensure the request includes a valid API key.");
        }

        return Task.FromResult(context.Value.UserId);
    }

    /// <inheritdoc />
    public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Guid?>(GetContextFromHttp()?.UserId);
    }

    private McpUserContext? GetContextFromHttp()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            _logger.LogWarning("MCP HTTP: no HttpContext available");
            return null;
        }

        if (!httpContext.Items.TryGetValue(UserIdItemKey, out var userIdObj)
            || userIdObj is not Guid userId
            || userId == Guid.Empty)
        {
            return null;
        }

        if (!httpContext.Items.TryGetValue(ScopesItemKey, out var scopesObj)
            || scopesObj is not ApiKeyScope scopes
            || !ApiKeyScopeRules.IsValid(scopes))
        {
            _logger.LogWarning("MCP HTTP: authenticated request has no valid API key scope context");
            return null;
        }

        return new McpUserContext(userId, scopes);
    }
}
