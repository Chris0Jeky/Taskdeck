using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Surfaces similar past proposal decisions for a given proposal,
/// providing reviewers with a historical base rate.
/// </summary>
public interface ISimilarDecisionService
{
    /// <summary>
    /// Gets the most recent similar past decisions for a proposal,
    /// matching on the proposal's primary action class.
    /// </summary>
    Task<Result<SimilarPastResultDto>> GetSimilarPastAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
