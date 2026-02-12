using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ArchiveItemRepository : Repository<ArchiveItem>, IArchiveItemRepository
{
    public ArchiveItemRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<ArchiveItem>> GetByBoardIdAsync(string boardId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(a => a.BoardId == boardId)
            .OrderByDescending(a => a.ArchivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ArchiveItem>> GetByEntityTypeAsync(string entityType, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(a => a.EntityType == entityType)
            .OrderByDescending(a => a.ArchivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ArchiveItem>> GetByStatusAsync(RestoreStatus status, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(a => a.Status == status)
            .OrderByDescending(a => a.ArchivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<ArchiveItem?> GetByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(a => a.EntityType == entityType && a.EntityId == entityId, cancellationToken);
    }

    public async Task<IEnumerable<ArchiveItem>> GetByUserIdAsync(string userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(a => a.ArchivedByUserId == userId)
            .OrderByDescending(a => a.ArchivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
