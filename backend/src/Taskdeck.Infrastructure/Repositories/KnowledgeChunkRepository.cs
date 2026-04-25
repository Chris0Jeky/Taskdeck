using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class KnowledgeChunkRepository : Repository<KnowledgeChunk>, IKnowledgeChunkRepository
{
    public KnowledgeChunkRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<KnowledgeChunk>> GetUnindexedBatchAsync(
        IReadOnlyCollection<Guid> indexedChunkIds,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();

        if (indexedChunkIds.Count > 0)
            query = query.Where(c => !indexedChunkIds.Contains(c.Id));

        return await query
            .OrderBy(c => c.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> candidateChunkIds,
        CancellationToken cancellationToken = default)
    {
        if (candidateChunkIds.Count == 0)
            return new HashSet<Guid>();

        var existingIds = await _dbSet
            .Where(c => candidateChunkIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        return existingIds.ToHashSet();
    }

    public async Task<int> CountUnindexedAsync(
        IReadOnlyCollection<Guid> indexedChunkIds,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();

        if (indexedChunkIds.Count > 0)
            query = query.Where(c => !indexedChunkIds.Contains(c.Id));

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IEnumerable<KnowledgeChunk>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        await _dbSet
            .Where(c => c.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
