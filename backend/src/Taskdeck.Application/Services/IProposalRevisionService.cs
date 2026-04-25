using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IProposalRevisionService
{
    /// <summary>
    /// Creates a new revision for a proposal. The proposal must be in PendingReview status.
    /// Revision numbers are auto-assigned (monotonically increasing).
    /// </summary>
    Task<Result<ProposalRevisionDto>> CreateRevisionAsync(CreateProposalRevisionDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all revisions for a proposal, ordered by revision number ascending.
    /// </summary>
    Task<Result<IReadOnlyList<ProposalRevisionDto>>> GetRevisionsForProposalAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest revision for a proposal, or null if no revisions exist.
    /// When no revisions exist, the original proposal payload is the effective payload.
    /// </summary>
    Task<Result<ProposalRevisionDto?>> GetLatestRevisionAsync(Guid proposalId, CancellationToken cancellationToken = default);
}
