using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IBoardEstimateRollupService
{
    Task<Result<BoardEstimateRollupDto>> GetAsync(Guid boardId, Guid actingUserId,
        CancellationToken cancellationToken = default);
}
