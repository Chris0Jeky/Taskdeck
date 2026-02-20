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

    public async Task<IEnumerable<CommandRun>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.RequestedByUserId == userId)
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

    public async Task<IEnumerable<CommandRunLog>> QueryLogsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? userId = null,
        string? correlationId = null,
        string? source = null,
        string? level = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CommandRunLogs
            .AsNoTracking()
            .Include(log => log.CommandRun)
            .Where(log => log.Timestamp >= from.UtcDateTime && log.Timestamp <= to.UtcDateTime);

        if (userId.HasValue)
        {
            query = query.Where(log => log.CommandRun.RequestedByUserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            var normalizedCorrelationId = correlationId.Trim().ToLower();
            query = query.Where(log => log.CommandRun.CorrelationId.ToLower() == normalizedCorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var normalizedSource = source.Trim().ToLower();
            query = query.Where(log => log.Source.ToLower() == normalizedSource);
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            var normalizedLevel = level.Trim().ToLower();
            query = query.Where(log => log.Level.ToLower() == normalizedLevel);
        }

        return await query
            .OrderByDescending(log => log.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
