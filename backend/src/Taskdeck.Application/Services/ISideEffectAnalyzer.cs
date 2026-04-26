using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Analyzes a proposal's operations to produce a 7-category side-effect breakdown
/// and a reversibility posture.
/// </summary>
public interface ISideEffectAnalyzer
{
    /// <summary>
    /// Analyzes the side effects of the specified proposal.
    /// </summary>
    Task<Result<ProposalSideEffectsDto>> AnalyzeAsync(Guid proposalId, CancellationToken cancellationToken = default);
}
