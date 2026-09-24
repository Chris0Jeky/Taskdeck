using Microsoft.AspNetCore.SignalR;
using Taskdeck.Api.Hubs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Realtime;

public sealed class SignalRBoardConnectionEvictor : IBoardConnectionEvictor
{
    private readonly IHubContext<BoardsHub> _hubContext;
    private readonly IBoardPresenceTracker _presenceTracker;

    public SignalRBoardConnectionEvictor(
        IHubContext<BoardsHub> hubContext,
        IBoardPresenceTracker presenceTracker)
    {
        _hubContext = hubContext;
        _presenceTracker = presenceTracker;
    }

    public async Task EvictUserFromBoardAsync(
        Guid boardId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var eviction = _presenceTracker.EvictUser(boardId, userId);
        if (eviction.EvictedConnectionIds.Count == 0)
            return;

        var group = BoardHubGroups.ForBoard(boardId);
        foreach (var connectionId in eviction.EvictedConnectionIds)
            await _hubContext.Groups.RemoveFromGroupAsync(connectionId, group, cancellationToken);

        await _hubContext.Clients
            .Group(group)
            .SendAsync("boardPresence", eviction.Snapshot, cancellationToken);

        await _hubContext.Clients
            .Clients(eviction.EvictedConnectionIds)
            .SendAsync("accessRevoked", new { boardId }, cancellationToken);
    }
}
