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

            // Id is a deterministic tiebreaker so offset paging stays consistent when
            // two notifications share the same CreatedAt (mirrors BoardRepository.SearchIdsAsync).
            sql.Append(" ORDER BY CreatedAt DESC, Id");
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
            .ThenBy(n => n.Id)
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

    /// <inheritdoc />
    /// <remarks>
    /// Uses raw SQL (ExecuteSqlRawAsync) which bypasses the EF Core change tracker.
    /// Callers must not query Notification entities into the same DbContext before
    /// calling this method, or the tracked entities will become stale.
    /// This is the same pattern used by <see cref="AuditLogRepository.DeleteOldEntriesAsync"/>.
    ///
    /// When called inside a transaction (e.g. AccountDeletionService), cancellation
    /// mid-batch is safe — the enclosing transaction rollback will undo partial deletes
    /// and the returned count will be discarded by the caller's catch block.
    /// </remarks>
    public async Task<int> DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const int batchSize = 1000;
        var totalDeleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            int deleted;
            if (_context.Database.IsSqlite())
            {
                deleted = await _context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM Notifications WHERE Id IN (SELECT Id FROM Notifications WHERE UserId = {0} LIMIT {1})",
                    new object[] { userId, batchSize },
                    cancellationToken);
            }
            else
            {
                // SQL Server: use a CTE with TOP for deterministic batch deletion.
                deleted = await _context.Database.ExecuteSqlRawAsync(
                    "WITH CTE AS (SELECT TOP({1}) Id FROM Notifications WHERE UserId = {0}) DELETE FROM Notifications WHERE Id IN (SELECT Id FROM CTE)",
                    new object[] { userId, batchSize },
                    cancellationToken);
            }

            totalDeleted += deleted;

            if (deleted < batchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }
}
