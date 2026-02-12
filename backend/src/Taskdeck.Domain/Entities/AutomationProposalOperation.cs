using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class AutomationProposalOperation : Entity
{
    public Guid ProposalId { get; private set; }
    public int Sequence { get; private set; }
    public string ActionType { get; private set; }
    public string TargetType { get; private set; }
    public string? TargetId { get; private set; }
    public string Parameters { get; private set; } // JSON payload
    public string IdempotencyKey { get; private set; }
    public string? ExpectedVersion { get; private set; }

    // Navigation
    public AutomationProposal Proposal { get; private set; } = null!;

    private AutomationProposalOperation() { } // EF Core

    public AutomationProposalOperation(
        Guid proposalId,
        int sequence,
        string actionType,
        string targetType,
        string parameters,
        string idempotencyKey,
        string? targetId = null,
        string? expectedVersion = null)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        if (sequence < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Sequence must be non-negative");
        if (string.IsNullOrWhiteSpace(actionType))
            throw new DomainException(ErrorCodes.ValidationError, "ActionType cannot be empty");
        if (string.IsNullOrWhiteSpace(targetType))
            throw new DomainException(ErrorCodes.ValidationError, "TargetType cannot be empty");
        if (string.IsNullOrWhiteSpace(parameters))
            throw new DomainException(ErrorCodes.ValidationError, "Parameters cannot be empty");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException(ErrorCodes.ValidationError, "IdempotencyKey cannot be empty");

        ProposalId = proposalId;
        Sequence = sequence;
        ActionType = actionType;
        TargetType = targetType;
        TargetId = targetId;
        Parameters = parameters;
        IdempotencyKey = idempotencyKey;
        ExpectedVersion = expectedVersion;
    }
}
