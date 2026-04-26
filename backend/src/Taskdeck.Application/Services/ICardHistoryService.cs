using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service for retrieving the card history ledger for a proposal's affected cards.
/// Used by the proposal review History section.
/// </summary>
public interface ICardHistoryService
{
    /// <summary>
    /// Returns a history ledger of all touches on cards affected by the given proposal,
    /// ordered by timestamp descending (newest first).
    /// </summary>
    Task<Result<IReadOnlyList<CardHistoryRowDto>>> GetCardHistoryForProposalAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default);
}
