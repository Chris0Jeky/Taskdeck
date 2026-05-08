using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Agents;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class McpToolHashRepository : Repository<McpToolHash>, IMcpToolHashRepository
{
    public McpToolHashRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<McpToolHash?> GetByUserAndToolAsync(
        Guid userId, string toolName, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(h => h.UserId == userId && h.ToolName == toolName, cancellationToken);
    }

    public async Task<IEnumerable<McpToolHash>> GetByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(h => h.UserId == userId)
            .OrderBy(h => h.ToolName)
            .ToListAsync(cancellationToken);
    }
}
