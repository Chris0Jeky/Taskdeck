using Taskdeck.Domain.Agents;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Repository for MCP tool definition hashes.
/// </summary>
public interface IMcpToolHashRepository
{
    Task<McpToolHash?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<McpToolHash?> GetByUserAndToolAsync(Guid userId, string toolName, CancellationToken cancellationToken = default);
    Task<IEnumerable<McpToolHash>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(McpToolHash entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(McpToolHash entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(McpToolHash entity, CancellationToken cancellationToken = default);
}
