using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Detects conflicts, warnings, and status signals for a proposal.
/// Returns a tone-classified list of rows for the review UI.
/// </summary>
public interface IProposalConflictDetector
{
    /// <summary>
    /// Detects conflicts using the server-authoritative, revision-resolved proposal snapshot
    /// already authorized by the API boundary.
    /// </summary>
    Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        ProposalDto effectiveProposal,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
