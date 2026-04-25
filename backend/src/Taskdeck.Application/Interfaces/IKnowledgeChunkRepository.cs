using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IKnowledgeChunkRepository : IRepository<KnowledgeChunk>
{
    Task<IEnumerable<KnowledgeChunk>> GetUnindexedBatchAsync(
        int processedOffset,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> candidateChunkIds,
        CancellationToken cancellationToken = default);

    Task<int> CountUnindexedAsync(
        int processedOffset,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<KnowledgeChunk>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
