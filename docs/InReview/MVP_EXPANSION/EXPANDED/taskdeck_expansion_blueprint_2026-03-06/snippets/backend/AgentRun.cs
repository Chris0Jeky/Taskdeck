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
    Cancelled = 8,
}

public sealed class AgentRun : Entity
{
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
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }

    private AgentRun() { }

    public AgentRun(Guid agentProfileId, Guid userId, string objective, Guid? boardId = null, string triggerType = "manual")
    {
        if (agentProfileId == Guid.Empty) throw new DomainException(ErrorCodes.ValidationError, "AgentProfileId cannot be empty.");
        if (userId == Guid.Empty) throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty.");
        if (string.IsNullOrWhiteSpace(objective)) throw new DomainException(ErrorCodes.ValidationError, "Objective is required.");

        AgentProfileId = agentProfileId;
        UserId = userId;
        Objective = objective.Trim();
        BoardId = boardId;
        TriggerType = string.IsNullOrWhiteSpace(triggerType) ? "manual" : triggerType.Trim();
    }

    public void TransitionTo(AgentRunStatus status, string? summary = null)
    {
        Status = status;
        Summary = string.IsNullOrWhiteSpace(summary) ? Summary : summary.Trim();
        if (status is AgentRunStatus.Completed or AgentRunStatus.Failed or AgentRunStatus.Cancelled)
        {
            CompletedAt = DateTimeOffset.UtcNow;
        }
        Touch();
    }

    public void AttachProposal(Guid proposalId, string? summary = null)
    {
        if (proposalId == Guid.Empty) throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty.");
        ProposalId = proposalId;
        Status = AgentRunStatus.ProposalCreated;
        Summary = summary ?? Summary;
        Touch();
    }

    public void MarkFailed(string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason)) throw new DomainException(ErrorCodes.ValidationError, "Failure reason is required.");
        FailureReason = failureReason.Trim();
        TransitionTo(AgentRunStatus.Failed, failureReason);
    }
}
