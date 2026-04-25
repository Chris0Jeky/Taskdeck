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
        int processedOffset,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var skip = Math.Max(0, processedOffset);

        return await _dbSet
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip(skip)
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
        int processedOffset,
        CancellationToken cancellationToken = default)
    {
        var total = await _dbSet.CountAsync(cancellationToken);
        return Math.Max(0, total - Math.Max(0, processedOffset));
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
