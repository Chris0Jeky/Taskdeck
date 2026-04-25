using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Records a content-free decision event for an automation proposal.
/// The entity stores structural outcome dimensions only, never proposal text,
/// user-entered rationale, or other business content.
/// </summary>
public class ProposalOutcome : Entity
{
    public Guid ProposalId { get; private set; }
    public Guid DecidedByUserId { get; private set; }
    public OutcomeDecision Decision { get; private set; }
    public OutcomeType OutcomeType { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
    public double DecisionLatencySeconds { get; private set; }
    public int FieldCount { get; private set; }
    public int EditedFieldCount { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public string RiskLevel { get; private set; } = string.Empty;
    public string? ModelId { get; private set; }
    public double? AverageFieldConfidence { get; private set; }

    public AutomationProposal Proposal { get; private set; } = null!;

    private ProposalOutcome() { } // EF Core

    public ProposalOutcome(
        Guid proposalId,
        OutcomeType outcomeType,
        Guid decidedByUserId)
        : this(
            proposalId,
            decidedByUserId,
            ToDecision(outcomeType),
            decisionLatencySeconds: 0.0,
            fieldCount: outcomeType == OutcomeType.EditedThenApproved ? 1 : 0,
            editedFieldCount: outcomeType == OutcomeType.EditedThenApproved ? 1 : 0,
            sourceType: "Unknown",
            riskLevel: "Unknown")
    {
    }

    public ProposalOutcome(
        Guid proposalId,
        Guid decidedByUserId,
        OutcomeDecision decision,
        double decisionLatencySeconds,
        int fieldCount,
        int editedFieldCount,
        string sourceType,
        string riskLevel,
        string? modelId = null,
        double? averageFieldConfidence = null)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        if (decidedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "DecidedByUserId cannot be empty");
        if (!Enum.IsDefined(decision))
            throw new DomainException(ErrorCodes.ValidationError, "OutcomeDecision value is invalid");
        if (!double.IsFinite(decisionLatencySeconds) || decisionLatencySeconds < 0)
            throw new DomainException(ErrorCodes.ValidationError, "DecisionLatencySeconds cannot be negative or non-finite");
        if (fieldCount < 0)
            throw new DomainException(ErrorCodes.ValidationError, "FieldCount cannot be negative");
        if (editedFieldCount < 0)
            throw new DomainException(ErrorCodes.ValidationError, "EditedFieldCount cannot be negative");
        if (editedFieldCount > fieldCount)
            throw new DomainException(ErrorCodes.ValidationError, "EditedFieldCount cannot exceed FieldCount");
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new DomainException(ErrorCodes.ValidationError, "SourceType cannot be empty");
        if (sourceType.Length > 50)
            throw new DomainException(ErrorCodes.ValidationError, "SourceType cannot exceed 50 characters");
        if (string.IsNullOrWhiteSpace(riskLevel))
            throw new DomainException(ErrorCodes.ValidationError, "RiskLevel cannot be empty");
        if (riskLevel.Length > 50)
            throw new DomainException(ErrorCodes.ValidationError, "RiskLevel cannot exceed 50 characters");
        if (modelId != null && modelId.Length > 100)
            throw new DomainException(ErrorCodes.ValidationError, "ModelId cannot exceed 100 characters");
        if (averageFieldConfidence.HasValue &&
            (!double.IsFinite(averageFieldConfidence.Value) ||
             averageFieldConfidence.Value < 0.0 ||
             averageFieldConfidence.Value > 1.0))
        {
            throw new DomainException(ErrorCodes.ValidationError, "AverageFieldConfidence must be between 0.0 and 1.0");
        }
        if (decision != OutcomeDecision.EditedThenApproved && editedFieldCount > 0)
            throw new DomainException(ErrorCodes.ValidationError, "EditedFieldCount must be 0 when decision is not EditedThenApproved");
        if (decision == OutcomeDecision.EditedThenApproved && editedFieldCount == 0)
            throw new DomainException(ErrorCodes.ValidationError, "EditedFieldCount must be greater than 0 when decision is EditedThenApproved");

        ProposalId = proposalId;
        DecidedByUserId = decidedByUserId;
        Decision = decision;
        OutcomeType = ToOutcomeType(decision);
        DecidedAt = DateTimeOffset.UtcNow;
        DecisionLatencySeconds = decisionLatencySeconds;
        FieldCount = fieldCount;
        EditedFieldCount = editedFieldCount;
        SourceType = sourceType;
        RiskLevel = riskLevel;
        ModelId = modelId;
        AverageFieldConfidence = averageFieldConfidence;
    }

    private static OutcomeType ToOutcomeType(OutcomeDecision decision)
    {
        return decision switch
        {
            OutcomeDecision.Approved => OutcomeType.Approved,
            OutcomeDecision.EditedThenApproved => OutcomeType.EditedThenApproved,
            OutcomeDecision.Rejected => OutcomeType.Rejected,
            OutcomeDecision.Ignored => OutcomeType.Ignored,
            _ => throw new DomainException(ErrorCodes.ValidationError, "OutcomeDecision value is invalid")
        };
    }

    private static OutcomeDecision ToDecision(OutcomeType outcomeType)
    {
        if (!Enum.IsDefined(typeof(OutcomeType), outcomeType))
            throw new DomainException(ErrorCodes.ValidationError, $"Invalid OutcomeType: {outcomeType}");

        return outcomeType switch
        {
            OutcomeType.Approved => OutcomeDecision.Approved,
            OutcomeType.EditedThenApproved => OutcomeDecision.EditedThenApproved,
            OutcomeType.Rejected => OutcomeDecision.Rejected,
            OutcomeType.Ignored => OutcomeDecision.Ignored,
            _ => throw new DomainException(ErrorCodes.ValidationError, $"Invalid OutcomeType: {outcomeType}")
        };
    }
}
