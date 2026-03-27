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

        List<Guid>? boardScopedEntityIdList = null;
        HashSet<Guid>? boardScopedEntityIdSet = null;
        if (boardId.HasValue)
        {
            boardScopedEntityIdList = await ResolveBoardScopedEntityIdsAsync(boardId.Value, cancellationToken);
            boardScopedEntityIdSet = new HashSet<Guid>(boardScopedEntityIdList);
        }

        if (_context.Database.IsSqlite())
        {
            var auditLogs = await _context.AuditLogs
                .FromSqlInterpolated($"SELECT * FROM AuditLogs WHERE Timestamp >= {from} AND Timestamp <= {to}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return auditLogs
                .Where(al => !userId.HasValue || al.UserId == userId.Value)
                .Where(al => boardScopedEntityIdSet == null || boardScopedEntityIdSet.Contains(al.EntityId))
                .Where(al =>
                    string.IsNullOrWhiteSpace(source) ||
                    al.EntityType.Equals(source.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(al => levelActions == null || levelActions.Contains(al.Action))
                .OrderByDescending(al => al.Timestamp)
                .Take(limit)
                .ToList();
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
            var normalizedSource = source.Trim().ToLower();
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
        var boardScopedEntityIds = await ResolveBoardScopedEntityIdsAsync(boardId, cancellationToken);
        var entityIdSet = new HashSet<Guid>(boardScopedEntityIds);

        if (_context.Database.IsSqlite())
        {
            var allLogs = await _context.AuditLogs
                .AsNoTracking()
                .Include(al => al.User)
                .ToListAsync(cancellationToken);

            return allLogs
                .Where(al => entityIdSet.Contains(al.EntityId))
                .OrderByDescending(al => al.Timestamp)
                .Take(limit)
                .ToList();
        }

        return await _context.AuditLogs
            .AsNoTracking()
            .Include(al => al.User)
            .Where(al => boardScopedEntityIds.Contains(al.EntityId))
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
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
