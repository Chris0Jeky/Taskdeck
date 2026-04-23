using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// SCAFFOLDING: Placeholder repository implementation for AuditLog entity.
/// </summary>
public class AuditLogRepository : Repository<AuditLog>, IAuditLogRepository
{
    public AuditLogRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<AuditLog>> QueryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? userId = null,
        Guid? boardId = null,
        string? source = null,
        string? level = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var levelActions = !string.IsNullOrWhiteSpace(level)
            ? GetActionsForLevel(level)
            : null;

        if (levelActions is { Length: 0 })
        {
            return Array.Empty<AuditLog>();
        }

        if (_context.Database.IsSqlite())
        {
            // SQLite does not support DateTimeOffset in LINQ WHERE/ORDER BY,
            // so we build a parameterised raw SQL query that pushes ALL filters
            // (userId, boardId, source, level) into the SQL WHERE clause using
            // EXISTS-based subqueries for board scope (no parameter explosion).
            return await BuildSqliteQueryAsync(
                from, to, userId, boardId, source, levelActions, limit, cancellationToken);
        }

        List<Guid>? boardScopedEntityIdList = null;
        if (boardId.HasValue)
        {
            boardScopedEntityIdList = await ResolveBoardScopedEntityIdsAsync(boardId.Value, cancellationToken);
        }

        var query = _context.AuditLogs
            .AsNoTracking()
            .Where(al => al.Timestamp >= from && al.Timestamp <= to);

        if (userId.HasValue)
        {
            query = query.Where(al => al.UserId == userId.Value);
        }

        if (boardScopedEntityIdList != null)
        {
            query = query.Where(al => boardScopedEntityIdList.Contains(al.EntityId));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var normalizedSource = source.Trim().ToLowerInvariant();
            query = query.Where(al => al.EntityType.ToLower() == normalizedSource);
        }

        if (levelActions != null)
        {
            query = query.Where(al => levelActions.Contains(al.Action));
        }

        return await query
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, int limit = 100, CancellationToken cancellationToken = default)
    {
        var normalizedEntityType = entityType.Trim().ToLowerInvariant();

        if (_context.Database.IsSqlite())
        {
            return await _context.AuditLogs
                .FromSqlInterpolated(
                    $"SELECT * FROM AuditLogs WHERE LOWER(EntityType) = {normalizedEntityType} AND EntityId = {entityId} ORDER BY Timestamp DESC LIMIT {limit}")
                .Include(al => al.User)
                .ToListAsync(cancellationToken);
        }

        return await _context.AuditLogs
            .Include(al => al.User)
            .Where(al => al.EntityType.ToLower() == normalizedEntityType && al.EntityId == entityId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetByUserAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.AuditLogs
                .FromSqlInterpolated(
                    $"SELECT * FROM AuditLogs WHERE UserId = {userId} ORDER BY Timestamp DESC LIMIT {limit}")
                .Include(al => al.User)
                .ToListAsync(cancellationToken);
        }

        return await _context.AuditLogs
            .Include(al => al.User)
            .Where(al => al.UserId == userId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetByBoardAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.AuditLogs
                .FromSqlInterpolated($"""
                    SELECT * FROM AuditLogs AS al
                    WHERE al.EntityId = {boardId}
                       OR EXISTS (SELECT 1 FROM Columns AS c WHERE c.Id = al.EntityId AND c.BoardId = {boardId})
                       OR EXISTS (SELECT 1 FROM Cards   AS c WHERE c.Id = al.EntityId AND c.BoardId = {boardId})
                       OR EXISTS (SELECT 1 FROM Labels  AS l WHERE l.Id = al.EntityId AND l.BoardId = {boardId})
                    ORDER BY al.Timestamp DESC
                    LIMIT {limit}
                    """)
                .AsNoTracking()
                .Include(al => al.User)
                .ToListAsync(cancellationToken);
        }

        var boardScopedEntityIds = await ResolveBoardScopedEntityIdsAsync(boardId, cancellationToken);

        return await _context.AuditLogs
            .AsNoTracking()
            .Include(al => al.User)
            .Where(al => boardScopedEntityIds.Contains(al.EntityId))
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Builds a parameterised raw SQL query for SQLite that pushes all filters
    /// (timestamp, userId, boardId via EXISTS subqueries, source, level/actions)
    /// into the SQL WHERE clause.  ORDER BY and LIMIT are also in the SQL so
    /// EF Core never attempts to translate DateTimeOffset ORDER BY via LINQ.
    ///
    /// Board-scope filtering uses EXISTS subqueries (matching
    /// <see cref="GetByBoardAsync"/>) instead of materialising entity IDs into
    /// an IN (...) clause, which avoids hitting SQLite's parameter limit and
    /// eliminates extra round-trips to resolve IDs.
    /// </summary>
    private async Task<List<AuditLog>> BuildSqliteQueryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? userId,
        Guid? boardId,
        string? source,
        AuditAction[]? levelActions,
        int limit,
        CancellationToken cancellationToken)
    {
        var conditions = new List<string> { "al.Timestamp >= {0} AND al.Timestamp <= {1}" };
        var parameters = new List<object> { from, to };
        var nextParam = 2;

        if (userId.HasValue)
        {
            conditions.Add($"al.UserId = {{{nextParam}}}");
            parameters.Add(userId.Value);
            nextParam++;
        }

        if (boardId.HasValue)
        {
            // Use EXISTS subqueries to scope to board entities without
            // materialising IDs (avoids SQLite parameter-count limits).
            conditions.Add(
                $"(al.EntityId = {{{nextParam}}}" +
                $" OR EXISTS (SELECT 1 FROM Columns AS c WHERE c.Id = al.EntityId AND c.BoardId = {{{nextParam}}})" +
                $" OR EXISTS (SELECT 1 FROM Cards   AS c WHERE c.Id = al.EntityId AND c.BoardId = {{{nextParam}}})" +
                $" OR EXISTS (SELECT 1 FROM Labels  AS l WHERE l.Id = al.EntityId AND l.BoardId = {{{nextParam}}}))");
            parameters.Add(boardId.Value);
            nextParam++;
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            conditions.Add($"LOWER(al.EntityType) = {{{nextParam}}}");
            parameters.Add(source.Trim().ToLowerInvariant());
            nextParam++;
        }

        if (levelActions is { Length: > 0 })
        {
            // AuditAction is stored as int; build an IN clause for the action values.
            // Action enum has a small fixed cardinality so IN is safe here.
            var actionPlaceholders = new List<string>(levelActions.Length);
            foreach (var action in levelActions)
            {
                actionPlaceholders.Add($"{{{nextParam}}}");
                parameters.Add((int)action);
                nextParam++;
            }
            conditions.Add($"al.Action IN ({string.Join(", ", actionPlaceholders)})");
        }

        var where = string.Join(" AND ", conditions);
        var sql = $"SELECT * FROM AuditLogs AS al WHERE {where} ORDER BY al.Timestamp DESC LIMIT {{{nextParam}}}";
        parameters.Add(limit);

        return await _context.AuditLogs
            .FromSqlRaw(sql, parameters.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Guid>> ResolveBoardScopedEntityIdsAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var columnIds = await _context.Columns
            .Where(c => c.BoardId == boardId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var cardIds = await _context.Cards
            .Where(c => c.BoardId == boardId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var labelIds = await _context.Labels
            .Where(l => l.BoardId == boardId)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        var ids = new List<Guid>(columnIds.Count + cardIds.Count + labelIds.Count + 1) { boardId };
        ids.AddRange(columnIds);
        ids.AddRange(cardIds);
        ids.AddRange(labelIds);
        return ids;
    }

    public async Task<int> DeleteOldEntriesAsync(DateTimeOffset olderThan, int batchSize, CancellationToken cancellationToken = default)
    {
        var totalDeleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Use parameterized SQL to delete in bounded batches.
            // SQLite and SQL Server both support this pattern.
            // The LIMIT/TOP clause prevents unbounded lock duration.
            var sql = _context.Database.IsSqlite()
                ? "DELETE FROM AuditLogs WHERE Id IN (SELECT Id FROM AuditLogs WHERE Timestamp < {0} LIMIT {1})"
                : "DELETE TOP({1}) FROM AuditLogs WHERE Timestamp < {0}";

            var deleted = await _context.Database.ExecuteSqlRawAsync(
                sql,
                new object[] { olderThan, batchSize },
                cancellationToken);

            totalDeleted += deleted;

            // If fewer rows were deleted than the batch size, we are done.
            if (deleted < batchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }

    private static AuditAction[] GetActionsForLevel(string level)
    {
        return level.Trim().ToLowerInvariant() switch
        {
            "info" =>
            [
                AuditAction.Created,
                AuditAction.Updated,
                AuditAction.Unarchived,
                AuditAction.Moved,
                AuditAction.PermissionGranted
            ],
            "warning" =>
            [
                AuditAction.Deleted,
                AuditAction.Archived,
                AuditAction.PermissionRevoked,
                AuditAction.OwnershipTransferred
            ],
            _ => []
        };
    }
}
