using Taskdeck.Domain.Agents;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Repository interface for MCP tool hash-pinning records.
/// </summary>
public interface IMcpToolHashRepository : IRepository<McpToolHash>
{
    /// <summary>
    /// Returns the hash record for a specific user and tool name, or null if not found.
    /// </summary>
    Task<McpToolHash?> GetByUserAndToolAsync(Guid userId, string toolName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all hash records for a given user.
    /// </summary>
    Task<IEnumerable<McpToolHash>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
