namespace Taskdeck.Application.Services;

public sealed record BoardRealtimeEvent(
    Guid BoardId,
    string EntityType,
    string Operation,
    Guid? EntityId,
    DateTimeOffset OccurredAt);
