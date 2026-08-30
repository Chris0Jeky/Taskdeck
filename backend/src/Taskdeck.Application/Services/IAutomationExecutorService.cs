using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

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
    Task<Result<ProposalExecutionReceipt>> ExecuteProposalWithReceiptAsync(
        Guid proposalId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
