using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class AutomationProposal : Entity
{
    public ProposalSourceType SourceType { get; private set; }
    public string? SourceReferenceId { get; private set; }
    public Guid? BoardId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public ProposalStatus Status { get; private set; }
    public RiskLevel RiskLevel { get; private set; }
    public string Summary { get; private set; }
    public string? DiffPreview { get; private set; }
    public string? ValidationIssues { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? AppliedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string CorrelationId { get; private set; }

    private readonly List<AutomationProposalOperation> _operations = new();
    public IReadOnlyList<AutomationProposalOperation> Operations => _operations.AsReadOnly();

    private AutomationProposal() { } // EF Core

    public AutomationProposal(
        ProposalSourceType sourceType,
        Guid requestedByUserId,
        string summary,
        RiskLevel riskLevel,
        string correlationId,
        Guid? boardId = null,
        string? sourceReferenceId = null,
        int expiryMinutes = 1440)
    {
        if (requestedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "RequestedByUserId cannot be empty");
        if (string.IsNullOrWhiteSpace(summary))
            throw new DomainException(ErrorCodes.ValidationError, "Summary cannot be empty");
        if (summary.Length > 500)
            throw new DomainException(ErrorCodes.ValidationError, "Summary cannot exceed 500 characters");
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new DomainException(ErrorCodes.ValidationError, "CorrelationId cannot be empty");
        if (expiryMinutes <= 0)
            throw new DomainException(ErrorCodes.ValidationError, "ExpiryMinutes must be positive");

        SourceType = sourceType;
        SourceReferenceId = sourceReferenceId;
        BoardId = boardId;
        RequestedByUserId = requestedByUserId;
        Status = ProposalStatus.PendingReview;
        RiskLevel = riskLevel;
        Summary = summary;
        CorrelationId = correlationId;
        ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);
    }

    public void AddOperation(AutomationProposalOperation operation)
    {
        if (Status != ProposalStatus.PendingReview)
            throw new DomainException(ErrorCodes.InvalidOperation, "Cannot add operations after proposal has been decided");
        
        _operations.Add(operation);
        Touch();
    }

    public void Approve(Guid decidedByUserId)
    {
        if (decidedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "DecidedByUserId cannot be empty");
        if (Status != ProposalStatus.PendingReview)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot approve proposal in status {Status}");
        if (DateTime.UtcNow > ExpiresAt)
            throw new DomainException(ErrorCodes.InvalidOperation, "Cannot approve expired proposal");

        Status = ProposalStatus.Approved;
        DecidedByUserId = decidedByUserId;
        DecidedAt = DateTime.UtcNow;
        Touch();
    }

    public void Reject(Guid decidedByUserId, string? reason = null)
    {
        if (decidedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "DecidedByUserId cannot be empty");
        if (Status != ProposalStatus.PendingReview)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot reject proposal in status {Status}");

        // High and Critical risk proposals require a reason for rejection
        if ((RiskLevel == RiskLevel.High || RiskLevel == RiskLevel.Critical) && string.IsNullOrWhiteSpace(reason))
            throw new DomainException(ErrorCodes.ValidationError, "Rejection reason is required for High and Critical risk proposals");

        Status = ProposalStatus.Rejected;
        DecidedByUserId = decidedByUserId;
        DecidedAt = DateTime.UtcNow;
        FailureReason = reason;
        Touch();
    }

    public void MarkAsApplied()
    {
        if (Status != ProposalStatus.Approved)
            throw new DomainException(ErrorCodes.InvalidOperation, "Only approved proposals can be marked as applied");

        Status = ProposalStatus.Applied;
        AppliedAt = DateTime.UtcNow;
        Touch();
    }

    public void MarkAsFailed(string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new DomainException(ErrorCodes.ValidationError, "FailureReason cannot be empty");
        if (Status != ProposalStatus.Approved)
            throw new DomainException(ErrorCodes.InvalidOperation, "Only approved proposals can be marked as failed");

        Status = ProposalStatus.Failed;
        FailureReason = failureReason;
        Touch();
    }

    public void Expire()
    {
        if (Status != ProposalStatus.PendingReview)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot expire proposal in status {Status}");

        Status = ProposalStatus.Expired;
        Touch();
    }

    public void SetDiffPreview(string diffPreview)
    {
        if (Status != ProposalStatus.PendingReview)
            throw new DomainException(ErrorCodes.InvalidOperation, "Cannot update diff preview after proposal has been decided");
        
        DiffPreview = diffPreview;
        Touch();
    }

    public void SetValidationIssues(string validationIssues)
    {
        if (Status != ProposalStatus.PendingReview)
            throw new DomainException(ErrorCodes.InvalidOperation, "Cannot update validation issues after proposal has been decided");
        
        ValidationIssues = validationIssues;
        Touch();
    }
}

public enum ProposalSourceType
{
    Queue,
    Chat,
    Manual
}

public enum ProposalStatus
{
    PendingReview,
    Approved,
    Rejected,
    Applied,
    Failed,
    Expired
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}
