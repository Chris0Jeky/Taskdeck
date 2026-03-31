using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class NotificationRepository : Repository<Notification>, INotificationRepository
{
    public NotificationRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Notification>> GetByUserIdAsync(
        Guid userId,
        int limit = 100,
        bool unreadOnly = false,
        Guid? boardId = null,
        CancellationToken cancellationToken = default,
        int offset = 0)
    {
        var query = _dbSet
            .Where(n => n.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        if (boardId.HasValue)
        {
            query = query.Where(n => n.BoardId == boardId.Value);
        }

        var notifications = await query.ToListAsync(cancellationToken);
        return notifications
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    public async Task<Notification?> GetByUserAndDeduplicationKeyAsync(
        Guid userId,
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(
            n => n.UserId == userId && n.DeduplicationKey == deduplicationKey,
            cancellationToken);
    }

    public async Task<IEnumerable<Notification>> GetUnreadByUserIdAsync(
        Guid userId,
        Guid? boardId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Where(n => n.UserId == userId && !n.IsRead);

        if (boardId.HasValue)
        {
            query = query.Where(n => n.BoardId == boardId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
