namespace Taskdeck.Application.DTOs;

public record ProposalRevisionDto(
    Guid Id,
    Guid ProposalId,
    int RevisionNumber,
    Guid EditorUserId,
    string RevisedPayload,
    DateTime RevisedAt,
    string Reason,
    DateTimeOffset CreatedAt
);

public record CreateProposalRevisionDto(
    Guid ProposalId,
    Guid EditorUserId,
    string RevisedPayload,
    string Reason
);

public record ProposalOutcomeDto(
    Guid Id,
    Guid ProposalId,
    string OutcomeType,
    Guid DecidedByUserId,
    DateTime DecidedAt,
    DateTimeOffset CreatedAt
);
