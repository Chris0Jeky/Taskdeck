using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
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

    public async Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.AuditLogs
                .FromSqlInterpolated(
                    $"SELECT * FROM AuditLogs WHERE EntityType = {entityType} AND EntityId = {entityId} ORDER BY Timestamp DESC LIMIT {limit}")
                .Include(al => al.User)
                .ToListAsync(cancellationToken);
        }

        return await _context.AuditLogs
            .Include(al => al.User)
            .Where(al => al.EntityType == entityType && al.EntityId == entityId)
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
                    $"SELECT * FROM AuditLogs WHERE EntityType = {"Board"} AND EntityId = {boardId} ORDER BY Timestamp DESC LIMIT {limit}")
                .Include(al => al.User)
                .ToListAsync(cancellationToken);
        }

        return await _context.AuditLogs
            .Include(al => al.User)
            .Where(al => al.EntityType == "Board" && al.EntityId == boardId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
