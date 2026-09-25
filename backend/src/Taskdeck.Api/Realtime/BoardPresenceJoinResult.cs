namespace Taskdeck.Api.Realtime;

/// <summary>
/// Outcome of <see cref="IBoardPresenceTracker.Join"/>.
/// </summary>
/// <param name="Snapshot">Presence roster of the joined board, including the joined connection.</param>
/// <param name="PreviousBoardSnapshot">
/// Post-removal roster of the board the connection just moved away from, or null when the
/// connection was not tracked on a different board (fresh join or same-board rejoin).
/// The hub publishes this so the old board's roster cannot go stale, and uses it to drop
/// the connection from the old board's SignalR group.
/// </param>
public sealed record BoardPresenceJoinResult(
    BoardPresenceSnapshot Snapshot,
    BoardPresenceSnapshot? PreviousBoardSnapshot);
