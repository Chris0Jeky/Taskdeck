using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;

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
    public Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetUserIdFromContext();
        if (userId is null)
        {
            throw new InvalidOperationException(
                "MCP HTTP: no authenticated user. Ensure the request includes a valid API key.");
        }
        return Task.FromResult(userId.Value);
    }

    /// <inheritdoc />
    public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetUserIdFromContext());
    }

    private Guid? GetUserIdFromContext()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            _logger.LogWarning("MCP HTTP: no HttpContext available");
            return null;
        }

        if (httpContext.Items.TryGetValue(UserIdItemKey, out var userIdObj) && userIdObj is Guid userId)
        {
            return userId;
        }

        return null;
    }
}
