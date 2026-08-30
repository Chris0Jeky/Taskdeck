using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services.Confidence;

/// <summary>
/// Reads source-labelled confidence values persisted with proposal provenance.
/// </summary>
public interface IConfidenceBreakdownService
{
    /// <summary>
    /// Returns exact model-reported/derived values, or an explicit deterministic/not-reported
    /// result with no numeric confidence.
    /// </summary>
    /// <param name="proposalId">The proposal to compute breakdown for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Result containing the breakdown DTO, or a failure result.</returns>
    Task<Result<ConfidenceBreakdownDto>> GetBreakdownAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default);
}
