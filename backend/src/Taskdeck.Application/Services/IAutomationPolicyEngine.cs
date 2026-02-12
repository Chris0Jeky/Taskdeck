using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public interface IAutomationPolicyEngine
{
    RiskLevel ClassifyRisk(IEnumerable<ProposalOperationDto> operations);
    Task<Result> ValidatePermissionsAsync(Guid userId, Guid? boardId, IEnumerable<ProposalOperationDto> operations, CancellationToken cancellationToken = default);
    Result ValidatePolicy(ProposalDto proposal);
}
