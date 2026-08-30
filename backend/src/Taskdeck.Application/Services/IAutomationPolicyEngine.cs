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
    /// set — board exists (NotFound) and requester clears <paramref name="accessBar"/> on the
    /// board (Forbidden). This is the single source of the access-gate codes and messages:
    /// <see cref="ValidatePermissionsAsync"/> composes it with the operation-contract validator
    /// for live previews and Apply, and the terminal stored-preview read (#1415) calls it directly
    /// because a decided proposal's historical preview must be access-gated WITHOUT re-validating
    /// its operations against live board state.
    /// </summary>
    /// <param name="accessBar">
    /// Which membership bar this lane needs — <see cref="BoardAccessBar.Write"/> for the mutation
    /// lanes (proposal creation, approve, execute), <see cref="BoardAccessBar.Read"/> for the read
    /// lanes (pending diff, terminal stored preview). Required, so every caller states its lane;
    /// see <see cref="BoardAccessBar"/> for why the two cannot share one bar (#1836).
    /// </param>
    Task<Result> ValidateBoardAccessAsync(Guid requesterUserId, Guid? boardId, BoardAccessBar accessBar, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects proposal decision writes for extant archived boards, then marks every tracked active
    /// board as participating in the caller's next save. Missing and null board references are left
    /// to the caller's existing validation contract. The batch shape validates every board before
    /// arming any marker so a mixed-board decision remains atomic.
    /// </summary>
    Task<Result> GuardProposalDecisionWritesAsync(
        IEnumerable<Guid?> boardIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes <see cref="ValidateBoardAccessAsync"/> (requester + board access at
    /// <paramref name="accessBar"/>) with the per-operation contract validator. The access gate
    /// runs for EVERY operation list, empty
    /// or not — only the per-operation contract checks are skipped when there are no operations,
    /// so an operation-less proposal is still board-access-gated (no empty-list short-circuit to
    /// Success with the board half skipped, #1426). Emptiness is not rejected here; the "at least
    /// one operation" structure gate (<see cref="ValidateOperationStructure"/>) owns that and runs
    /// before this method in every approve/apply/diff chain.
    /// </summary>
    /// <param name="accessBar">
    /// Passed straight through to <see cref="ValidateBoardAccessAsync"/>; the operation-contract
    /// half is bar-independent. Approve and Apply pass <see cref="BoardAccessBar.Write"/>; the
    /// diff read path passes <see cref="BoardAccessBar.Read"/>, which is the ONLY gate difference
    /// between preview and Apply (#1836) — see the call sites in
    /// <c>AutomationProposalService.GetProposalDiffAsync</c>.
    /// </param>
    Task<Result> ValidatePermissionsAsync(Guid userId, Guid? boardId, IEnumerable<ProposalOperationDto> operations, BoardAccessBar accessBar, CancellationToken cancellationToken = default);

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
