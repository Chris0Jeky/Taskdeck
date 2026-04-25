using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IKnowledgeChunkRepository : IRepository<KnowledgeChunk>
{
    Task<IEnumerable<KnowledgeChunk>> GetUnindexedBatchAsync(
        KnowledgeChunkBackfillCursor? cursor,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> candidateChunkIds,
        CancellationToken cancellationToken = default);

    Task<int> CountUnindexedAsync(
        KnowledgeChunkBackfillCursor? cursor,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<KnowledgeChunk>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeChunkBackfillCursor(
    DateTimeOffset CreatedAt,
    Guid Id);
