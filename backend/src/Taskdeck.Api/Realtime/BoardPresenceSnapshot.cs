namespace Taskdeck.Api.Realtime;

public sealed record BoardPresenceMember(
    Guid UserId,
    string? DisplayName,
    Guid? EditingCardId);

public sealed record BoardPresenceSnapshot(
    Guid BoardId,
    IReadOnlyList<BoardPresenceMember> Members,
    DateTimeOffset OccurredAt);
