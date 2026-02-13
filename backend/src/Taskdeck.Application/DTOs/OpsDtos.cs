using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record CommandRunDto(
    Guid Id,
    string TemplateName,
    Guid RequestedByUserId,
    CommandRunStatus Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int? ExitCode,
    bool Truncated,
    string CorrelationId,
    string? ErrorMessage,
    string? OutputPreview,
    DateTimeOffset CreatedAt
);

public record CommandRunDetailDto(
    Guid Id,
    string TemplateName,
    Guid RequestedByUserId,
    CommandRunStatus Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int? ExitCode,
    bool Truncated,
    string CorrelationId,
    string? ErrorMessage,
    string? OutputPreview,
    DateTimeOffset CreatedAt,
    List<CommandRunLogDto> Logs
);

public record CommandRunLogDto(
    Guid Id,
    Guid CommandRunId,
    DateTime Timestamp,
    string Level,
    string Source,
    string Message,
    string? Metadata
);

public record RunCommandDto(
    string TemplateName,
    Dictionary<string, string>? Parameters = null
);

public record CommandTemplateDto(
    string Name,
    string Description,
    string RiskClass,
    int TimeoutSeconds,
    string RequiredRole,
    List<string> AcceptedParameters
);
