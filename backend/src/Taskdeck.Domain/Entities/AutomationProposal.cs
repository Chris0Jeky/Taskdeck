using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class AutomationProposal : Entity
{
    /// <summary>Default snooze window applied when the caller does not supply an override.</summary>
    public const int DefaultDeferMinutes = 60;

    /// <summary>Upper bound (24h) on a single defer window; the override is clamped to this.</summary>
    public const int MaxDeferMinutes = 1440;

    /// <summary>
    /// Grace period added on top of <see cref="DeferredUntil"/> when pushing out
    /// <see cref="ExpiresAt"/>, so a snoozed proposal can never silently expire while
    /// it is still snoozed (review-first / no-silent-expiry).
    /// </summary>
    public static readonly TimeSpan DeferExpiryGrace = TimeSpan.FromHours(24);

    public ProposalSourceType SourceType { get; private set; }
    public string? SourceReferenceId { get; private set; }
    public Guid? BoardId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public ProposalStatus Status { get; private set; }
    public RiskLevel RiskLevel { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string? DiffPreview { get; private set; }
    public string? ValidationIssues { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? DeferredUntil { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? AppliedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;

    private readonly List<AutomationProposalOperation> _operations = new();
    public IReadOnlyList<AutomationProposalOperation> Operations => _operations.AsReadOnly();

    private readonly List<ProposalRevision> _revisions = new();
    public IReadOnlyList<ProposalRevision> Revisions => _revisions.AsReadOnly();

    private readonly List<ProposalOutcome> _outcomes = new();
    public IReadOnlyList<ProposalOutcome> Outcomes => _outcomes.AsReadOnly();

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
        // A decided proposal must never carry a stale snooze that would hide it from list reads.
        DeferredUntil = null;
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
        // A decided proposal must never carry a stale snooze that would hide it from list reads.
        DeferredUntil = null;
        Touch();
    }

    public void MarkAsApplied()
    {
        if (Status != ProposalStatus.Approved)
            throw new DomainException(ErrorCodes.InvalidOperation, "Only approved proposals can be marked as applied");

        Status = ProposalStatus.Applied;
        AppliedAt = DateTime.UtcNow;
        // Terminal proposal: clear any residual snooze so list reads never hide it.
        DeferredUntil = null;
        Touch();
    }

    /// <summary>
    /// Snoozes a pending proposal for <paramref name="duration"/>. Defer is a timing
    /// control, not a status change: the proposal stays <see cref="ProposalStatus.PendingReview"/>
    /// and undecided. <see cref="ExpiresAt"/> is pushed beyond <see cref="DeferredUntil"/>
    /// (plus <see cref="DeferExpiryGrace"/>) so a snoozed proposal cannot silently expire.
    /// Re-deferring is intentionally unbounded (each call resets the window).
    /// </summary>
    public void Defer(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(MaxDeferMinutes))
            throw new DomainException(ErrorCodes.ValidationError, $"Defer duration must be between 1 minute and {MaxDeferMinutes} minutes");
        if (Status != ProposalStatus.PendingReview)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot defer proposal in status {Status}");
        if (DateTime.UtcNow > ExpiresAt)
            throw new DomainException(ErrorCodes.InvalidOperation, "Cannot defer expired proposal");

        var now = DateTime.UtcNow;
        var deferredUntil = now + duration;
        DeferredUntil = deferredUntil;
        var floor = deferredUntil + DeferExpiryGrace;
        if (ExpiresAt < floor)
            ExpiresAt = floor;
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
        // Terminal proposal: clear any residual snooze so list reads never hide it.
        DeferredUntil = null;
        Touch();
    }

    /// <summary>
    /// True when the proposal's expiry time has passed, regardless of the current status.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>
    /// True when the proposal is currently snoozed (a future <see cref="DeferredUntil"/> is set).
    /// </summary>
    public bool IsDeferred => DeferredUntil.HasValue && DateTime.UtcNow < DeferredUntil.Value;

    /// <summary>
    /// True when the proposal is in a state that allows dismissal.
    /// Terminal statuses (Applied, Rejected, Failed, Expired) and approved-but-expired proposals can be dismissed.
    /// </summary>
    public bool CanBeDismissed =>
        Status is ProposalStatus.Applied or ProposalStatus.Rejected
            or ProposalStatus.Failed or ProposalStatus.Expired
        || (Status == ProposalStatus.Approved && IsExpired);

    public void Dismiss()
    {
        if (!CanBeDismissed)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot dismiss proposal in status {Status}");

        Status = ProposalStatus.Dismissed;
        // Terminal proposal: clear any residual snooze so list reads never hide it.
        DeferredUntil = null;
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
    Expired,
    Dismissed
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}
