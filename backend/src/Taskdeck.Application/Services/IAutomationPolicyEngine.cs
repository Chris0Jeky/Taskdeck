using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public interface IAutomationPolicyEngine
{
    RiskLevel ClassifyRisk(IEnumerable<ProposalOperationDto> operations);
    Task<Result> ValidatePermissionsAsync(Guid userId, Guid? boardId, IEnumerable<ProposalOperationDto> operations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the structural invariants of a proposal's operations — at least one operation,
    /// operation count within <c>MaxOperationCount</c>, unique and non-negative sequences, and
    /// parameters within <c>MaxParametersLength</c>. Does NOT check expiry (that is proposal-level,
    /// see <see cref="ValidatePolicy"/>). Reusable by both apply-time policy validation and the
    /// revision-save path so a saved revision cannot be structurally unexecutable (#1281).
    /// </summary>
    Result ValidateOperationStructure(IReadOnlyCollection<ProposalOperationDto> operations);

    Result ValidatePolicy(ProposalDto proposal);
}
