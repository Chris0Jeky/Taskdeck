using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record ProposalDto(
    Guid Id,
    ProposalSourceType SourceType,
    string? SourceReferenceId,
    Guid? BoardId,
    Guid RequestedByUserId,
    ProposalStatus Status,
    RiskLevel RiskLevel,
    string Summary,
    string? DiffPreview,
    string? ValidationIssues,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTime ExpiresAt,
    DateTime? DecidedAt,
    Guid? DecidedByUserId,
    DateTime? AppliedAt,
    string? FailureReason,
    string CorrelationId,
    List<ProposalOperationDto> Operations
);

public record ProposalOperationDto(
    Guid Id,
    Guid ProposalId,
    int Sequence,
    string ActionType,
    string TargetType,
    string? TargetId,
    string Parameters,
    string IdempotencyKey,
    string? ExpectedVersion
);

public record CreateProposalDto(
    ProposalSourceType SourceType,
    Guid RequestedByUserId,
    string Summary,
    RiskLevel RiskLevel,
    string CorrelationId,
    Guid? BoardId = null,
    string? SourceReferenceId = null,
    int ExpiryMinutes = 1440,
    List<CreateProposalOperationDto>? Operations = null
);

public record CreateProposalOperationDto(
    int Sequence,
    string ActionType,
    string TargetType,
    string Parameters,
    string IdempotencyKey,
    string? TargetId = null,
    string? ExpectedVersion = null
);

public record UpdateProposalStatusDto(
    string? Reason = null
);

public record ProposalFilterDto(
    ProposalStatus? Status = null,
    Guid? BoardId = null,
    Guid? UserId = null,
    RiskLevel? RiskLevel = null,
    int Limit = 100
);
