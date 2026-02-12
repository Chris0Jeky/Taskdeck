using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class CommandRunRepository : Repository<CommandRun>, ICommandRunRepository
{
    public CommandRunRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<CommandRun>> GetByUserIdAsync(string userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<CommandRun>> GetByStatusAsync(CommandRunStatus status, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<CommandRun>> GetByTemplateNameAsync(string templateName, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.TemplateName == templateName)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<CommandRun?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(c => c.CorrelationId == correlationId, cancellationToken);
    }

    public async Task<CommandRun?> GetByIdWithLogsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.Logs)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
}
