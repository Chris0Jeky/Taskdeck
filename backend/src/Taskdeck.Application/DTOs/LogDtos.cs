namespace Taskdeck.Application.DTOs;

public record LogEntryDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string Level,
    string Source,
    string EventName,
    string Message,
    string? CorrelationId,
    Guid? UserId,
    Guid? BoardId,
    string? Metadata
);

public record LogQueryDto(
    string? Level = null,
    string? Source = null,
    Guid? UserId = null,
    Guid? BoardId = null,
    string? CorrelationId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Limit = 100
);

public record LogStreamEvent(
    string EventType,
    LogEntryDto? Entry = null
);
