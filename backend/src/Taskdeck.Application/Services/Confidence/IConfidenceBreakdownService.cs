using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services.Confidence;

/// <summary>
/// Computes a multi-component confidence breakdown for a proposal
/// so the review UI can explain why a proposal is or isn't above the apply threshold.
/// </summary>
public interface IConfidenceBreakdownService
{
    /// <summary>
    /// Computes the confidence breakdown for the given proposal.
    /// </summary>
    /// <param name="proposalId">The proposal to compute breakdown for.</param>
    /// <param name="userId">The calling user (for authorization context).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Result containing the breakdown DTO, or a failure result.</returns>
    Task<Result<ConfidenceBreakdownDto>> GetBreakdownAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
