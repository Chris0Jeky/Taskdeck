using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// The approved-revision pin a batch caller explicitly consented to. The record's presence is
/// load-bearing: an instance whose <see cref="ApprovedRevisionId"/> is null means "the fresh
/// proposal must still be unpinned", while no expectation at all preserves the single-execute
/// contract.
/// </summary>
public sealed record ProposalExecutionRevisionExpectation(Guid? ApprovedRevisionId);

public interface IAutomationExecutorService
{
    Task<Result> ExecuteProposalAsync(Guid proposalId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same execution as <see cref="ExecuteProposalAsync"/>, returning the receipt detail the
    /// per-proposal batch surface reports (#1307). <see cref="ExecuteProposalAsync"/> is a thin
    /// projection of this call, so single execute and batch execute share one code path and one
    /// materialization of the approved revision — preview == apply parity holds per proposal by
    /// construction, not by a parallel implementation.
    /// </summary>
    /// <param name="callerUserId">
    /// The human whose request this is, when that differs from the proposal's original requester.
    /// Supplied, it is rechecked against the target board's WRITE bar inside the execution's own
    /// transaction, immediately before any operation runs. Batch execute must pass it: a batch
    /// takes one authorization reading before its loop, so without a transactional recheck a
    /// collaborator whose access is revoked part-way through would keep applying, and an
    /// owner-authored proposal would still apply for a submitter who has since lost access - the
    /// pre-loop reading having been taken when they still had it. Null means "no separate caller",
    /// which is the single-execute case where the endpoint authorized the same request moments
    /// earlier; the requester-side check below runs either way.
    /// </param>
    Task<Result<ProposalExecutionReceipt>> ExecuteProposalWithReceiptAsync(
        Guid proposalId,
        string idempotencyKey,
        Guid? callerUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch execution overload. The revision expectation is checked against the same fresh
    /// proposal materialization that owns status/idempotency, before any operation or linked-capture
    /// synchronization. Keeping this separate from the overload above means single execute has no
    /// accidental implicit null-pin expectation.
    /// </summary>
    Task<Result<ProposalExecutionReceipt>> ExecuteProposalWithReceiptAsync(
        Guid proposalId,
        string idempotencyKey,
        Guid? callerUserId,
        ProposalExecutionRevisionExpectation revisionExpectation,
        CancellationToken cancellationToken = default);
}
