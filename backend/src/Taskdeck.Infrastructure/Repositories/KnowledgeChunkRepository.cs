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
        KnowledgeChunkBackfillCursor? cursor,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (cursor is null)
        {
            return await _dbSet
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        var sameTimestamp = await _dbSet
            .Where(c => c.CreatedAt == cursor.CreatedAt)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var page = sameTimestamp
            .Where(c => IsAfterCursor(c, cursor))
            .Take(batchSize)
            .ToList();

        if (page.Count >= batchSize)
            return page;

        var newer = await _dbSet
            .Where(c => c.CreatedAt > cursor.CreatedAt)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Take(batchSize - page.Count)
            .ToListAsync(cancellationToken);

        page.AddRange(newer);
        return page;
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
        KnowledgeChunkBackfillCursor? cursor,
        CancellationToken cancellationToken = default)
    {
        if (cursor is null)
            return await _dbSet.CountAsync(cancellationToken);

        var sameTimestamp = await _dbSet
            .Where(c => c.CreatedAt == cursor.CreatedAt)
            .ToListAsync(cancellationToken);

        var sameTimestampRemaining = sameTimestamp.Count(c => IsAfterCursor(c, cursor));
        var newerRemaining = await _dbSet
            .Where(c => c.CreatedAt > cursor.CreatedAt)
            .CountAsync(cancellationToken);

        return sameTimestampRemaining + newerRemaining;
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

    private static bool IsAfterCursor(KnowledgeChunk chunk, KnowledgeChunkBackfillCursor cursor)
    {
        if (chunk.CreatedAt > cursor.CreatedAt)
            return true;

        return chunk.CreatedAt == cursor.CreatedAt && chunk.Id.CompareTo(cursor.Id) > 0;
    }
}
