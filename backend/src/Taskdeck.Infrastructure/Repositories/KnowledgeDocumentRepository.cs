using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class KnowledgeDocumentRepository : Repository<KnowledgeDocument>, IKnowledgeDocumentRepository
{
    public KnowledgeDocumentRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<KnowledgeDocument>> GetByUserIdAsync(
        Guid userId,
        Guid? boardId = null,
        bool includeArchived = false,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await GetByUserIdSqliteAsync(userId, boardId, includeArchived, limit, offset, cancellationToken);
        }

        var query = _dbSet.Where(d => d.UserId == userId);

        if (!includeArchived)
            query = query.Where(d => !d.IsArchived);

        if (boardId.HasValue)
            query = query.Where(d => d.BoardId == boardId.Value);

        return await query
            .OrderByDescending(d => d.UpdatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<KnowledgeDocument>> GetByBoardIdAsync(
        Guid boardId,
        bool includeArchived = false,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate DateTimeOffset ordering from LINQ; use raw SQL.
            if (includeArchived)
            {
                return await _dbSet
                    .FromSqlInterpolated(
                        $"SELECT * FROM KnowledgeDocuments WHERE BoardId = {boardId} ORDER BY UpdatedAt DESC LIMIT {limit} OFFSET {offset}")
                    .ToListAsync(cancellationToken);
            }

            return await _dbSet
                .FromSqlInterpolated(
                    $"SELECT * FROM KnowledgeDocuments WHERE BoardId = {boardId} AND IsArchived = 0 ORDER BY UpdatedAt DESC LIMIT {limit} OFFSET {offset}")
                .ToListAsync(cancellationToken);
        }

        var query = _dbSet.Where(d => d.BoardId == boardId);

        if (!includeArchived)
            query = query.Where(d => !d.IsArchived);

        return await query
            .OrderByDescending(d => d.UpdatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<IEnumerable<KnowledgeDocument>> GetByUserIdSqliteAsync(
        Guid userId,
        Guid? boardId,
        bool includeArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        // SQLite cannot translate DateTimeOffset ordering from LINQ; use raw SQL.
        if (boardId.HasValue && !includeArchived)
        {
            return await _dbSet
                .FromSqlInterpolated(
                    $"SELECT * FROM KnowledgeDocuments WHERE UserId = {userId} AND BoardId = {boardId.Value} AND IsArchived = 0 ORDER BY UpdatedAt DESC LIMIT {limit} OFFSET {offset}")
                .ToListAsync(cancellationToken);
        }

        if (boardId.HasValue)
        {
            return await _dbSet
                .FromSqlInterpolated(
                    $"SELECT * FROM KnowledgeDocuments WHERE UserId = {userId} AND BoardId = {boardId.Value} ORDER BY UpdatedAt DESC LIMIT {limit} OFFSET {offset}")
                .ToListAsync(cancellationToken);
        }

        if (!includeArchived)
        {
            return await _dbSet
                .FromSqlInterpolated(
                    $"SELECT * FROM KnowledgeDocuments WHERE UserId = {userId} AND IsArchived = 0 ORDER BY UpdatedAt DESC LIMIT {limit} OFFSET {offset}")
                .ToListAsync(cancellationToken);
        }

        return await _dbSet
            .FromSqlInterpolated(
                $"SELECT * FROM KnowledgeDocuments WHERE UserId = {userId} ORDER BY UpdatedAt DESC LIMIT {limit} OFFSET {offset}")
            .ToListAsync(cancellationToken);
    }
}
