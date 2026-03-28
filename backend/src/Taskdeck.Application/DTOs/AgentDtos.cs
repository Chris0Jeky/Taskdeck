using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record AgentProfileDto(
    Guid Id,
    Guid UserId,
    string Name,
    string Description,
    string TemplateKey,
    AgentScopeType ScopeType,
    Guid? ScopeBoardId,
    string PolicyJson,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateAgentProfileDto(
    string Name,
    string TemplateKey,
    AgentScopeType ScopeType,
    Guid? ScopeBoardId = null,
    string? Description = null,
    string? PolicyJson = null);

public record UpdateAgentProfileDto(
    string Name,
    string? Description = null,
    string? PolicyJson = null,
    bool? IsEnabled = null);

public record AgentRunDto(
    Guid Id,
    Guid AgentProfileId,
    Guid UserId,
    Guid? BoardId,
    string TriggerType,
    string Objective,
    AgentRunStatus Status,
    string? Summary,
    string? FailureReason,
    Guid? ProposalId,
    int StepsExecuted,
    int TokensUsed,
    decimal? ApproxCostUsd,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record AgentRunDetailDto(
    Guid Id,
    Guid AgentProfileId,
    Guid UserId,
    Guid? BoardId,
    string TriggerType,
    string Objective,
    AgentRunStatus Status,
    string? Summary,
    string? FailureReason,
    Guid? ProposalId,
    int StepsExecuted,
    int TokensUsed,
    decimal? ApproxCostUsd,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<AgentRunEventDto> Events);

public record AgentRunEventDto(
    Guid Id,
    Guid RunId,
    int SequenceNumber,
    string EventType,
    string Payload,
    DateTimeOffset Timestamp);

public record CreateAgentRunDto(
    string Objective,
    Guid? BoardId = null);
