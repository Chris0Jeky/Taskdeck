using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IAutomationPlannerService
{
    Task<Result<ProposalDto>> ParseInstructionAsync(string instruction, Guid userId, Guid? boardId = null, CancellationToken cancellationToken = default);
}
