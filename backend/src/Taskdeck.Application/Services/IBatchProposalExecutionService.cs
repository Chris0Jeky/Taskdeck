using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Per-proposal batch execute (#1307, q-14 C). Each selected proposal is executed independently
/// through the single-execute code path, in its own transaction, and reports its own outcome.
/// There is no whole-batch rollback and no whole-batch atomicity — that is the ruled contract,
/// not an accident of implementation.
/// </summary>
public interface IBatchProposalExecutionService
{
    Task<Result<BatchExecuteProposalsResultDto>> ExecuteProposalsAsync(
        IReadOnlyList<BatchExecuteProposalSelectionDto> selections,
        Guid callerUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The read-only fields phase one needs to shape authorization and approved-revision outcomes.
/// Status is deliberately absent: the per-item executor owns the fresh persisted status and
/// idempotency decision immediately before it considers any operation.
/// </summary>
public sealed record ProposalExecutionAuthorizationSnapshot(
    Guid ProposalId,
    Guid? BoardId,
    Guid RequestedByUserId,
    Guid? ApprovedRevisionId);

/// <summary>
/// Reads phase-one batch authorization snapshots without attaching proposal entities to the
/// request persistence context. A snapshot must never poison the executor's later fresh lookup.
/// </summary>
public interface IProposalExecutionAuthorizationSnapshotReader
{
    Task<ProposalExecutionAuthorizationSnapshot?> FindAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default);
}
