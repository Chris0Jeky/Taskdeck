using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public interface IAutomationProposalService
{
    /// <summary>
    /// Creates a new automation proposal with operations.
    /// </summary>
    Task<Result<ProposalDto>> CreateProposalAsync(CreateProposalDto dto, CancellationToken cancellationToken = default);

    /// <summary>Creates a proposal and attaches trusted transcript evidence to its operation fields.</summary>
    Task<Result<ProposalDto>> CreateTranscriptProposalAsync(
        CreateProposalDto dto,
        IReadOnlyList<TranscriptEvidenceLinkInput> evidence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a proposal by ID with all operations.
    /// </summary>
    Task<Result<ProposalDto>> GetProposalByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets proposals with optional filters.
    /// </summary>
    Task<Result<IEnumerable<ProposalDto>>> GetProposalsAsync(ProposalFilterDto? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending proposal.
    /// </summary>
    Task<Result<ProposalDto>> ApproveProposalAsync(Guid id, Guid decidedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves an explicit bounded set of fresh, low-risk, create-card-only proposals atomically.
    /// The complete set is revalidated before any proposal transitions, and this method never executes
    /// the approved operations.
    /// </summary>
    Task<Result<BatchApproveProposalsResultDto>> ApproveProposalsAsync(
        IReadOnlyList<Guid> ids,
        Guid decidedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a pending proposal with optional reason (required for High/Critical risk).
    /// </summary>
    Task<Result<ProposalDto>> RejectProposalAsync(Guid id, Guid decidedByUserId, UpdateProposalStatusDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Snoozes a pending proposal for the given duration. Defer is a timing control,
    /// not a decision: the proposal stays PendingReview and no outcome/notification is written.
    /// </summary>
    Task<Result<ProposalDto>> DeferProposalAsync(Guid id, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an approved proposal as applied.
    /// </summary>
    Task<Result<ProposalDto>> MarkAsAppliedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an approved proposal as failed with reason.
    /// </summary>
    Task<Result<ProposalDto>> MarkAsFailedAsync(Guid id, string failureReason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Expires all stale pending proposals.
    /// </summary>
    Task<Result<int>> ExpireProposalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the diff preview for a proposal.
    /// </summary>
    Task<Result<string>> GetProposalDiffAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serves the STORED diff preview for a decided (terminal) proposal — Applied, Rejected,
    /// Failed, Expired, or Dismissed — after re-running ONLY the requester/board-access half of
    /// the gate, via the shared <c>IAutomationPolicyEngine.ValidateBoardAccessAsync</c> at the
    /// <c>BoardAccessBar.Read</c> bar (requester exists → 404, board exists → 404, requester is a
    /// board member → 403; #1398/#1413, bar pinned by #1836 — this is a read, so membership is the
    /// bar and a member demoted to Viewer keeps access to their own proposals' preview). A
    /// reviewer who lost board access, or whose board/requester was deleted, is therefore denied
    /// the stored preview rather than reading stale board contents through it (#1415). The
    /// operation-contract validator and the pre-decision structure/expiry gates are deliberately
    /// NOT run: they no longer apply once a proposal is decided, and re-validating a historical
    /// preview against live board state would wrongly deny it when referenced entities were later
    /// deleted, or always for an Applied create whose TargetId now resolves (the #1397 decision:
    /// terminal previews are historical, never rebuilt or re-checked against the moving board).
    /// A never-stored preview is returned as null (not empty) so callers can distinguish absence.
    /// Non-terminal proposals must use <see cref="GetProposalDiffAsync"/> instead.
    /// </summary>
    Task<Result<string>> GetTerminalProposalStoredPreviewAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismisses completed proposals (Applied, Rejected, Failed, Expired) so they no longer appear in the default review list.
    /// </summary>
    Task<Result<int>> DismissProposalsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
}
