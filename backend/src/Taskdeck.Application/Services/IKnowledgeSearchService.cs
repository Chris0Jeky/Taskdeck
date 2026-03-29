using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

public interface IKnowledgeSearchService
{
    Task<IEnumerable<KnowledgeSearchResultDto>> SearchAsync(
        string query,
        Guid userId,
        Guid? boardId = null,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task UpdateFtsIndexAsync(
        Guid documentId,
        string title,
        string content,
        CancellationToken cancellationToken = default);

    Task DeleteFtsIndexAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
