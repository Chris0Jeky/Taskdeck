using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

public interface IProposalFeedbackService
{
    /// <summary>
    /// Records a content-free "bad suggestion" signal for a proposal. Idempotent per
    /// (proposal, user): a repeat is a no-op, except a later specific reason upgrades an
    /// earlier Unspecified one (last-specific-wins). Never changes the proposal's status.
    /// Returns NotFound when the proposal does not exist.
    /// </summary>
    Task<Result> ReportBadSuggestionAsync(
        Guid proposalId,
        Guid reportedByUserId,
        ProposalFeedbackReason reason,
        CancellationToken cancellationToken = default);
}
