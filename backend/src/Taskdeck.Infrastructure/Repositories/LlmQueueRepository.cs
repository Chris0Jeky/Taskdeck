using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// SCAFFOLDING: Placeholder repository implementation for LlmRequest entity.
/// </summary>
public class LlmQueueRepository : Repository<LlmRequest>, ILlmQueueRepository
{
    public LlmQueueRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == RequestStatus.Pending)
            .OrderBy(lr => lr.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.LlmRequests
            .Include(lr => lr.Board)
            .Where(lr => lr.UserId == userId)
            .OrderByDescending(lr => lr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<LlmRequest>> GetByStatusAsync(RequestStatus status, CancellationToken cancellationToken = default)
    {
        return await _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == status)
            .OrderByDescending(lr => lr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default)
    {
        return await _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == RequestStatus.Pending)
            .OrderBy(lr => lr.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
