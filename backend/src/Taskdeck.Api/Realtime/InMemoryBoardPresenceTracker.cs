namespace Taskdeck.Api.Realtime;

public sealed class InMemoryBoardPresenceTracker : IBoardPresenceTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, ConnectionPresence>> _connectionsByBoard = new();
    private readonly Dictionary<string, Guid> _boardByConnection = new();

    public BoardPresenceSnapshot Join(
        Guid boardId,
        string connectionId,
        Guid userId,
        string? displayName)
    {
        lock (_gate)
        {
            if (!_connectionsByBoard.TryGetValue(boardId, out var boardConnections))
            {
                boardConnections = new Dictionary<string, ConnectionPresence>(StringComparer.Ordinal);
                _connectionsByBoard[boardId] = boardConnections;
            }

            boardConnections[connectionId] = new ConnectionPresence(userId, displayName, EditingCardId: null);
            _boardByConnection[connectionId] = boardId;

            return CreateSnapshot(boardId, boardConnections);
        }
    }

    public BoardPresenceSnapshot Leave(Guid boardId, string connectionId)
    {
        lock (_gate)
        {
            if (!_connectionsByBoard.TryGetValue(boardId, out var boardConnections))
            {
                return new BoardPresenceSnapshot(boardId, [], DateTimeOffset.UtcNow);
            }

            boardConnections.Remove(connectionId);
            _boardByConnection.Remove(connectionId);

            if (boardConnections.Count == 0)
            {
                _connectionsByBoard.Remove(boardId);
                return new BoardPresenceSnapshot(boardId, [], DateTimeOffset.UtcNow);
            }

            return CreateSnapshot(boardId, boardConnections);
        }
    }

    public BoardPresenceSnapshot? LeaveConnection(string connectionId)
    {
        lock (_gate)
        {
            if (!_boardByConnection.TryGetValue(connectionId, out var boardId))
            {
                return null;
            }

            return Leave(boardId, connectionId);
        }
    }

    public BoardPresenceSnapshot? UpdateEditingCard(Guid boardId, string connectionId, Guid? editingCardId)
    {
        lock (_gate)
        {
            if (!_connectionsByBoard.TryGetValue(boardId, out var boardConnections))
            {
                return null;
            }

            if (!boardConnections.TryGetValue(connectionId, out var connection))
            {
                return null;
            }

            boardConnections[connectionId] = connection with { EditingCardId = editingCardId };
            return CreateSnapshot(boardId, boardConnections);
        }
    }

    public bool IsConnectionJoinedBoard(string connectionId, Guid boardId)
    {
        lock (_gate)
        {
            return _boardByConnection.TryGetValue(connectionId, out var joinedBoardId)
                && joinedBoardId == boardId;
        }
    }

    private static BoardPresenceSnapshot CreateSnapshot(
        Guid boardId,
        Dictionary<string, ConnectionPresence> boardConnections)
    {
        var members = boardConnections.Values
            .GroupBy(connection => connection.UserId)
            .Select(group =>
            {
                var displayName = group
                    .Select(item => item.DisplayName)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var editingCardId = group
                    .Select(item => item.EditingCardId)
                    .FirstOrDefault(cardId => cardId.HasValue);

                return new BoardPresenceMember(group.Key, displayName, editingCardId);
            })
            .OrderBy(member => member.DisplayName ?? member.UserId.ToString("N"))
            .ToList();

        return new BoardPresenceSnapshot(boardId, members, DateTimeOffset.UtcNow);
    }

    private sealed record ConnectionPresence(
        Guid UserId,
        string? DisplayName,
        Guid? EditingCardId);
}
