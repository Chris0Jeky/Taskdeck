using System.Text;
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
        // Clamp to non-negative so paging stays bounded. A negative LIMIT in SQLite
        // means "no limit", which would silently restore the unbounded fetch this fix removes.
        var boundedLimit = limit < 0 ? 0 : limit;
        var boundedOffset = offset < 0 ? 0 : offset;

        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate DateTimeOffset ORDER BY from LINQ (see ADR-0023),
            // so push ordering + paging into raw SQL. This keeps ORDER BY + LIMIT/OFFSET in
            // the database (newest first) instead of materializing every matching row and
            // sorting/slicing in memory. Mirrors the AgentRunRepository SQLite pattern.
            var sql = new StringBuilder("SELECT * FROM Notifications WHERE UserId = {0}");
            var parameters = new List<object> { userId };

            if (unreadOnly)
            {
                sql.Append(" AND IsRead = 0");
            }

            if (boardId.HasValue)
            {
                sql.Append(" AND BoardId = {").Append(parameters.Count).Append('}');
                parameters.Add(boardId.Value);
            }

            sql.Append(" ORDER BY CreatedAt DESC");
            sql.Append(" LIMIT {").Append(parameters.Count).Append('}');
            parameters.Add(boundedLimit);
            sql.Append(" OFFSET {").Append(parameters.Count).Append('}');
            parameters.Add(boundedOffset);

            return await _dbSet
                .FromSqlRaw(sql.ToString(), parameters.ToArray())
                .ToListAsync(cancellationToken);
        }

        // Non-SQLite providers (e.g. PostgreSQL) translate DateTimeOffset ordering natively,
        // so keep the strongly-typed LINQ query and push ORDER BY + Skip/Take into SQL.
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

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(boundedOffset)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
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
