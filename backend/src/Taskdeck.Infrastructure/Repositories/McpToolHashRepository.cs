using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Agents;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class McpToolHashRepository : IMcpToolHashRepository
{
    private readonly TaskdeckDbContext _context;
    private readonly DbSet<McpToolHash> _dbSet;

    public McpToolHashRepository(TaskdeckDbContext context)
    {
        _context = context;
        _dbSet = context.Set<McpToolHash>();
    }

    public async Task<McpToolHash?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<McpToolHash?> GetByUserAndToolAsync(Guid userId, string toolName, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(h => h.UserId == userId && h.ToolName == toolName, cancellationToken);
    }

    public async Task<IEnumerable<McpToolHash>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(h => h.UserId == userId)
            .OrderBy(h => h.ToolName)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(McpToolHash entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
    }

    public Task UpdateAsync(McpToolHash entity, CancellationToken cancellationToken = default)
    {
        _context.Entry(entity).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(McpToolHash entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }
}
