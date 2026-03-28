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
}
