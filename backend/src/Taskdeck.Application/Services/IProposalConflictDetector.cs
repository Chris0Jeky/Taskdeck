using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Detects conflicts, warnings, and status signals for a proposal.
/// Returns a tone-classified list of rows for the review UI.
/// </summary>
public interface IProposalConflictDetector
{
    Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
