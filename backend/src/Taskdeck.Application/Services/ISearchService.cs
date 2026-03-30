using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface ISearchService
{
    Task<Result<GlobalSearchResultDto>> SearchAsync(
        Guid userId,
        string query,
        int maxResults = 20,
        CancellationToken cancellationToken = default);
}
