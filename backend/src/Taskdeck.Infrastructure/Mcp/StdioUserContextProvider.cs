using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Mcp;

/// <summary>
/// Resolves user identity for stdio MCP sessions.
/// In stdio mode the process runs under the OS user's identity, so no network
/// authentication is needed. We map to the configured default local user or,
/// if none is configured, to the first non-system user found in the database.
/// </summary>
public class StdioUserContextProvider : IUserContextProvider
{
    private readonly Guid _userId;

    public StdioUserContextProvider(IConfiguration configuration, IUnitOfWork unitOfWork, ILogger<StdioUserContextProvider> logger)
    {
        var configuredId = configuration["McpServer:DefaultUserId"];
        if (configuredId is not null && Guid.TryParse(configuredId, out var parsed))
        {
            _userId = parsed;
            logger.LogInformation("MCP stdio: using configured DefaultUserId {UserId}", _userId);
            return;
        }

        // Fall back to the first user in the database (single-user local-first scenario).
        var users = unitOfWork.Users.GetAllAsync().GetAwaiter().GetResult();
        var firstUser = users.FirstOrDefault();
        if (firstUser is null)
            throw new InvalidOperationException(
                "MCP stdio: no users found in database and McpServer:DefaultUserId is not configured. " +
                "Run the app in web mode first to create a user, then configure McpServer:DefaultUserId.");

        _userId = firstUser.Id;
        logger.LogInformation("MCP stdio: resolved to first local user {UserId} ({Username})", _userId, firstUser.Username);
    }

    /// <inheritdoc />
    public Guid GetCurrentUserId() => _userId;

    /// <inheritdoc />
    public bool TryGetCurrentUserId(out Guid userId)
    {
        userId = _userId;
        return _userId != Guid.Empty;
    }
}
