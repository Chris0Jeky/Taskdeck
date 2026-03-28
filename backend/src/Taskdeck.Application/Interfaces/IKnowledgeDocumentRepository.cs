using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IKnowledgeDocumentRepository : IRepository<KnowledgeDocument>
{
    Task<IEnumerable<KnowledgeDocument>> GetByUserIdAsync(
        Guid userId,
        Guid? boardId = null,
        bool includeArchived = false,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<KnowledgeDocument>> GetByBoardIdAsync(
        Guid boardId,
        bool includeArchived = false,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);
}
