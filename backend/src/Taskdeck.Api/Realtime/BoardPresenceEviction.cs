namespace Taskdeck.Api.Realtime;

public sealed record BoardPresenceEviction(
    BoardPresenceSnapshot Snapshot,
    IReadOnlyList<string> EvictedConnectionIds);
