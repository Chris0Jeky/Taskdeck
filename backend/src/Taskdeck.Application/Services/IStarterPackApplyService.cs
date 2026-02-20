using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IStarterPackApplyService
{
    Task<Result<StarterPackApplyResultDto>> ApplyToBoardAsync(
        Guid boardId,
        ApplyStarterPackDto dto,
        CancellationToken cancellationToken = default);
}
