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
        var query = _dbSet.Where(d => d.BoardId == boardId);

        if (!includeArchived)
            query = query.Where(d => !d.IsArchived);

        return await query
            .OrderByDescending(d => d.UpdatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
