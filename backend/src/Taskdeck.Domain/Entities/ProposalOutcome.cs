using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Records the user's decision on a proposal with content-free dimensions.
/// Designed for outcome tracking, feedback loops, and quality metrics.
///
/// IMPORTANT: This entity must never store user content, proposal text, or PII.
/// Only structural/dimensional data (decision type, timing, field counts) is recorded.
/// </summary>
public class ProposalOutcome : Entity
{
    /// <summary>
    /// The proposal this outcome records a decision for.
    /// </summary>
    public Guid ProposalId { get; private set; }

    /// <summary>
    /// The user who made the decision.
    /// </summary>
    public Guid DecidedByUserId { get; private set; }

    /// <summary>
    /// What decision was made.
    /// </summary>
    public OutcomeDecision Decision { get; private set; }

    /// <summary>
    /// Time in seconds between proposal creation and the user's decision.
    /// </summary>
    public double DecisionLatencySeconds { get; private set; }

    /// <summary>
    /// Number of fields in the proposal at time of decision.
    /// </summary>
    public int FieldCount { get; private set; }

    /// <summary>
    /// Number of fields that were edited before approval (only meaningful for EditedThenApproved).
    /// </summary>
    public int EditedFieldCount { get; private set; }

    /// <summary>
    /// The source type of the proposal (mirrors ProposalSourceType for denormalized querying).
    /// Stored as string to avoid coupling this ledger to proposal lifecycle enums.
    /// </summary>
    public string SourceType { get; private set; } = string.Empty;

    /// <summary>
    /// Risk level at the time of the decision (denormalized for analytics).
    /// Stored as string to avoid coupling.
    /// </summary>
    public string RiskLevel { get; private set; } = string.Empty;

    /// <summary>
    /// Model ID that generated the proposal (denormalized from provenance).
    /// </summary>
    public string? ModelId { get; private set; }

    /// <summary>
    /// Average confidence of provenance fields at time of decision.
    /// Null if no provenance was attached.
    /// </summary>
    public double? AverageFieldConfidence { get; private set; }

    private ProposalOutcome() { } // EF Core

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
        if (averageFieldConfidence.HasValue && (!double.IsFinite(averageFieldConfidence.Value) || averageFieldConfidence.Value < 0.0 || averageFieldConfidence.Value > 1.0))
            throw new DomainException(ErrorCodes.ValidationError, "AverageFieldConfidence must be between 0.0 and 1.0");
        if (decision != OutcomeDecision.EditedThenApproved && editedFieldCount > 0)
            throw new DomainException(ErrorCodes.ValidationError, "EditedFieldCount must be 0 when decision is not EditedThenApproved");
        if (decision == OutcomeDecision.EditedThenApproved && editedFieldCount == 0)
            throw new DomainException(ErrorCodes.ValidationError, "EditedFieldCount must be greater than 0 when decision is EditedThenApproved");

        ProposalId = proposalId;
        DecidedByUserId = decidedByUserId;
        Decision = decision;
        DecisionLatencySeconds = decisionLatencySeconds;
        FieldCount = fieldCount;
        EditedFieldCount = editedFieldCount;
        SourceType = sourceType;
        RiskLevel = riskLevel;
        ModelId = modelId;
        AverageFieldConfidence = averageFieldConfidence;
    }
}
