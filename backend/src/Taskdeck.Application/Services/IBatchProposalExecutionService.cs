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
