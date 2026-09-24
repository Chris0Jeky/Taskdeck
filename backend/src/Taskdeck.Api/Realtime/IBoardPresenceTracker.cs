namespace Taskdeck.Api.Realtime;

public interface IBoardPresenceTracker
{
    BoardPresenceSnapshot Join(
        Guid boardId,
        string connectionId,
        Guid userId,
        string? displayName);

    BoardPresenceSnapshot Leave(Guid boardId, string connectionId);

    BoardPresenceSnapshot? LeaveConnection(string connectionId);

    BoardPresenceSnapshot? UpdateEditingCard(Guid boardId, string connectionId, Guid? editingCardId);

    bool IsConnectionJoinedBoard(string connectionId, Guid boardId);

    /// <summary>
    /// Atomically removes every connection of <paramref name="userId"/> from
    /// <paramref name="boardId"/>, returning the removed connection ids and the
    /// presence snapshot after removal (#3420/#3407).
    /// </summary>
    BoardPresenceEviction EvictUser(Guid boardId, Guid userId);
}
