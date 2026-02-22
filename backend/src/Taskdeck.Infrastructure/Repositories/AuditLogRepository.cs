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
            var auditLogs = await _context.AuditLogs
                .FromSqlInterpolated($"SELECT * FROM AuditLogs WHERE Timestamp >= {from} AND Timestamp <= {to}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return auditLogs
                .Where(al => !userId.HasValue || al.UserId == userId.Value)
                .Where(al =>
                    !boardId.HasValue ||
                    (al.EntityId == boardId.Value && al.EntityType.Equals("board", StringComparison.OrdinalIgnoreCase)))
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

        if (boardId.HasValue)
        {
            query = query
                .Where(al => al.EntityId == boardId.Value)
                .Where(al => al.EntityType.ToLower() == "board");
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
        if (_context.Database.IsSqlite())
        {
            return await _context.AuditLogs
                .FromSqlInterpolated(
                    $"SELECT * FROM AuditLogs WHERE LOWER(EntityType) = {"board"} AND EntityId = {boardId} ORDER BY Timestamp DESC LIMIT {limit}")
                .Include(al => al.User)
                .ToListAsync(cancellationToken);
        }

        return await _context.AuditLogs
            .Include(al => al.User)
            .Where(al => al.EntityType.ToLower() == "board" && al.EntityId == boardId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
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
