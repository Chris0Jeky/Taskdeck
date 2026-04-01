using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Mcp;

/// <summary>
/// Resolves user identity for stdio MCP sessions.
/// In stdio mode the process runs under the OS user's identity, so no network
/// authentication is needed. We map to the configured default local user or,
/// if none is configured, to the first user found in the database.
/// </summary>
public class StdioUserContextProvider : IUserContextProvider
{
    private readonly Guid? _configuredUserId;
    private readonly TaskdeckDbContext _dbContext;
    private readonly ILogger<StdioUserContextProvider> _logger;

    /// <summary>Lazily resolved user ID (null = not yet resolved).</summary>
    private Guid? _resolvedUserId;

    public StdioUserContextProvider(
        IConfiguration configuration,
        TaskdeckDbContext dbContext,
        ILogger<StdioUserContextProvider> logger)
    {
        _dbContext = dbContext;
        _logger = logger;

        var configuredId = configuration["McpServer:DefaultUserId"];
        if (configuredId is not null
            && Guid.TryParse(configuredId, out var parsed)
            && parsed != Guid.Empty)
        {
            _configuredUserId = parsed;
            _logger.LogInformation("MCP stdio: using configured DefaultUserId {UserId}", parsed);
        }
    }

    /// <inheritdoc />
    public async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = await GetUserIdAsync(cancellationToken);
        return userId ?? throw new InvalidOperationException(
            "MCP stdio: no users found in database and McpServer:DefaultUserId is not configured. " +
            "Run the app in web mode first to create a user, then configure McpServer:DefaultUserId.");
    }

    /// <inheritdoc />
    public async Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (_resolvedUserId.HasValue)
            return _resolvedUserId.Value;

        if (_configuredUserId.HasValue)
        {
            _resolvedUserId = _configuredUserId;
            return _resolvedUserId;
        }

        // Fall back to the first user in the database (single-user local-first scenario).
        // Uses a targeted query (LIMIT 1) rather than loading all users into memory.
        var firstUser = await _dbContext.Users
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (firstUser is null || firstUser == Guid.Empty)
            return null;

        _resolvedUserId = firstUser;
        _logger.LogInformation("MCP stdio: resolved to first local user {UserId}", firstUser);
        return _resolvedUserId;
    }
}
