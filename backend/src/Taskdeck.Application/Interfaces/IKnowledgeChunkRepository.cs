using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IKnowledgeChunkRepository : IRepository<KnowledgeChunk>
{
    Task<IEnumerable<KnowledgeChunk>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task DeleteByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
