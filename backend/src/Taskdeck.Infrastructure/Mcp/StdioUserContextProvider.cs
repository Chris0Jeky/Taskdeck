using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Mcp;

/// <summary>
/// Resolves user identity for stdio MCP sessions.
/// In stdio mode the process runs under the OS user's identity, so no network
/// authentication is available. We map to an explicitly configured active
/// local user or, when configuration is absent, the only active local user.
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
        if (configuredId is null)
            return;

        if (!Guid.TryParse(configuredId, out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidOperationException(
                "MCP stdio: McpServer:DefaultUserId must be a non-empty GUID when configured. " +
                "Correct the value or remove it; without it, exactly one active local user is required.");
        }

        _configuredUserId = parsed;
    }

    /// <inheritdoc />
    public async Task<McpUserContext> GetCurrentContextAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return new McpUserContext(userId, ApiKeyScope.Full);
    }

    /// <inheritdoc />
    public async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = await GetUserIdAsync(cancellationToken);
        return userId ?? throw new InvalidOperationException(
            "MCP stdio: user identity resolution returned no result.");
    }

    /// <inheritdoc />
    public async Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (_resolvedUserId.HasValue)
            return _resolvedUserId.Value;

        if (_configuredUserId.HasValue)
        {
            var exists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(
                    u => u.Id == _configuredUserId.Value && u.IsActive,
                    cancellationToken);

            if (!exists)
            {
                throw new InvalidOperationException(
                    "MCP stdio: McpServer:DefaultUserId does not identify an active local user. " +
                    "Set it to an existing active user ID before starting stdio MCP.");
            }

            _resolvedUserId = _configuredUserId.Value;
            _logger.LogInformation(
                "MCP stdio: resolved configured DefaultUserId {UserId}",
                _resolvedUserId.Value);
            return _resolvedUserId.Value;
        }

        var activeUserIds = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => u.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (activeUserIds.Count == 0)
        {
            throw new InvalidOperationException(
                "MCP stdio: no active local users are available. " +
                "Run the app in web mode and create or reactivate a user before starting stdio MCP.");
        }

        if (activeUserIds.Count > 1)
        {
            throw new InvalidOperationException(
                "MCP stdio: multiple active local users exist. " +
                "Configure McpServer:DefaultUserId (environment variable McpServer__DefaultUserId) " +
                "with the intended active user ID before starting stdio MCP.");
        }

        _resolvedUserId = activeUserIds[0];
        _logger.LogInformation(
            "MCP stdio: resolved the only active local user {UserId}",
            _resolvedUserId.Value);
        return _resolvedUserId.Value;
    }
}
