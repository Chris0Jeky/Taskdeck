using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public enum AgentRunStatus
{
    Queued = 0,
    GatheringContext = 1,
    Planning = 2,
    ProposalCreated = 3,
    WaitingForReview = 4,
    Applying = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8
}

public sealed class AgentRun : Entity
{
    private const int MaxObjectiveLength = 2000;
    private const int MaxSummaryLength = 4000;
    private const int MaxFailureReasonLength = 4000;
    private const int MaxTriggerTypeLength = 50;

    private readonly List<AgentRunEvent> _events = new();

    public Guid AgentProfileId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? BoardId { get; private set; }
    public string TriggerType { get; private set; } = "manual";
    public string Objective { get; private set; } = string.Empty;
    public AgentRunStatus Status { get; private set; } = AgentRunStatus.Queued;
    public string? Summary { get; private set; }
    public string? FailureReason { get; private set; }
    public Guid? ProposalId { get; private set; }
    public int StepsExecuted { get; private set; }
    public int TokensUsed { get; private set; }
    public decimal? ApproxCostUsd { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyCollection<AgentRunEvent> Events => _events.AsReadOnly();

    private AgentRun() : base() { } // EF Core

    public AgentRun(
        Guid agentProfileId,
        Guid userId,
        string objective,
        string triggerType = "manual",
        Guid? boardId = null)
        : base()
    {
        if (agentProfileId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "AgentProfileId cannot be empty");

        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (string.IsNullOrWhiteSpace(objective))
            throw new DomainException(ErrorCodes.ValidationError, "Objective cannot be empty");

        if (objective.Length > MaxObjectiveLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Objective cannot exceed {MaxObjectiveLength} characters");

        if (string.IsNullOrWhiteSpace(triggerType))
            throw new DomainException(ErrorCodes.ValidationError, "TriggerType cannot be empty");

        if (triggerType.Length > MaxTriggerTypeLength)
            throw new DomainException(ErrorCodes.ValidationError, $"TriggerType cannot exceed {MaxTriggerTypeLength} characters");

        AgentProfileId = agentProfileId;
        UserId = userId;
        Objective = objective;
        TriggerType = triggerType;
        BoardId = boardId;
        Status = AgentRunStatus.Queued;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void TransitionTo(AgentRunStatus status, string? summary = null)
    {
        if (Status == AgentRunStatus.Completed || Status == AgentRunStatus.Failed || Status == AgentRunStatus.Cancelled)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot transition from terminal status {Status}");

        if (summary is not null && summary.Length > MaxSummaryLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Summary cannot exceed {MaxSummaryLength} characters");

        Status = status;

        if (summary is not null)
            Summary = summary;

        if (status is AgentRunStatus.Completed or AgentRunStatus.Failed or AgentRunStatus.Cancelled)
            CompletedAt = DateTimeOffset.UtcNow;

        Touch();
    }

    public void AttachProposal(Guid proposalId, string? summary = null)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");

        if (summary is not null && summary.Length > MaxSummaryLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Summary cannot exceed {MaxSummaryLength} characters");

        ProposalId = proposalId;

        if (summary is not null)
            Summary = summary;

        Touch();
    }

    public void MarkFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(ErrorCodes.ValidationError, "FailureReason cannot be empty");

        if (reason.Length > MaxFailureReasonLength)
            throw new DomainException(ErrorCodes.ValidationError, $"FailureReason cannot exceed {MaxFailureReasonLength} characters");

        Status = AgentRunStatus.Failed;
        FailureReason = reason;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void IncrementSteps(int count = 1)
    {
        StepsExecuted += count;
        Touch();
    }

    public void AddTokenUsage(int tokens, decimal? costUsd = null)
    {
        TokensUsed += tokens;
        if (costUsd.HasValue)
            ApproxCostUsd = (ApproxCostUsd ?? 0m) + costUsd.Value;
        Touch();
    }
}
