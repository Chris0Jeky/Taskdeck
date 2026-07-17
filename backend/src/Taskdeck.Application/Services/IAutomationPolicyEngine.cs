using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public interface IAutomationPolicyEngine
{
    RiskLevel ClassifyRisk(IEnumerable<ProposalOperationDto> operations);

    /// <summary>
    /// The shared requester/board half of the permission gate: requester non-empty
    /// (ValidationError), requester exists (NotFound), and — when <paramref name="boardId"/> is
    /// set — board exists (NotFound) and requester has board access (Forbidden). This is the
    /// single source of the access-gate codes and messages: <see cref="ValidatePermissionsAsync"/>
    /// composes it with the operation-contract validator for live previews and Apply, and the
    /// terminal stored-preview read (#1415) calls it directly because a decided proposal's
    /// historical preview must be access-gated WITHOUT re-validating its operations against
    /// live board state.
    /// </summary>
    Task<Result> ValidateBoardAccessAsync(Guid requesterUserId, Guid? boardId, CancellationToken cancellationToken = default);

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
