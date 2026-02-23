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
}
