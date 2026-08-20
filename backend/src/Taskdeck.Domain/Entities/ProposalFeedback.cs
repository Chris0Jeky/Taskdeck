using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A content-free negative-feedback signal: a reviewer flagged a proposal as a bad or
/// unhelpful suggestion. Orthogonal to the proposal's decision lifecycle -- recording
/// feedback never changes <see cref="AutomationProposal.Status"/>. The entity stores only
/// structural dimensions (which proposal, which user, a reason category) and deliberately
/// has NO free-text field, so the no-PII invariant is impossible to violate by construction.
/// At most one feedback row exists per (proposal, user); see the unique index in
/// ProposalFeedbackConfiguration.
/// </summary>
public class ProposalFeedback : Entity
{
    public Guid ProposalId { get; private set; }
    public Guid ReportedByUserId { get; private set; }
    public ProposalFeedbackReason Reason { get; private set; }
    public DateTimeOffset ReportedAt { get; private set; }

    private ProposalFeedback() { } // EF Core

    public ProposalFeedback(Guid proposalId, Guid reportedByUserId, ProposalFeedbackReason reason)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        if (reportedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ReportedByUserId cannot be empty");
        if (!Enum.IsDefined(reason))
            throw new DomainException(ErrorCodes.ValidationError, "ProposalFeedbackReason value is invalid");

        ProposalId = proposalId;
        ReportedByUserId = reportedByUserId;
        Reason = reason;
        ReportedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates the reason on an existing feedback row. The report flow applies
    /// first-specific-wins: a first one-click report stores <see cref="ProposalFeedbackReason.Unspecified"/>;
    /// the first later categorized report can upgrade it without creating a second row.
    /// </summary>
    public void UpdateReason(ProposalFeedbackReason reason)
    {
        if (!Enum.IsDefined(reason))
            throw new DomainException(ErrorCodes.ValidationError, "ProposalFeedbackReason value is invalid");

        Reason = reason;
        Touch();
    }
}
