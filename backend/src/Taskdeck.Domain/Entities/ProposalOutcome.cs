using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Records a decision event for an automation proposal.
/// Content-free: captures the outcome type and who decided, not business rationale.
/// Immutable after creation.
/// </summary>
public class ProposalOutcome : Entity
{
    /// <summary>FK to the AutomationProposal this outcome applies to.</summary>
    public Guid ProposalId { get; private set; }

    /// <summary>The type of decision outcome.</summary>
    public OutcomeType OutcomeType { get; private set; }

    /// <summary>The user who made the decision.</summary>
    public Guid DecidedByUserId { get; private set; }

    /// <summary>When the decision was recorded (UTC).</summary>
    public DateTime DecidedAt { get; private set; }

    // Navigation
    public AutomationProposal Proposal { get; private set; } = null!;

    private ProposalOutcome() { } // EF Core

    public ProposalOutcome(
        Guid proposalId,
        OutcomeType outcomeType,
        Guid decidedByUserId)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        if (decidedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "DecidedByUserId cannot be empty");
        if (!Enum.IsDefined(typeof(OutcomeType), outcomeType))
            throw new DomainException(ErrorCodes.ValidationError, $"Invalid OutcomeType: {outcomeType}");

        ProposalId = proposalId;
        OutcomeType = outcomeType;
        DecidedByUserId = decidedByUserId;
        DecidedAt = DateTime.UtcNow;
    }
}
