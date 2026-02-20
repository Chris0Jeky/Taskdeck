using Microsoft.AspNetCore.SignalR;
using Taskdeck.Api.Hubs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Realtime;

public class SignalRBoardRealtimeNotifier : IBoardRealtimeNotifier
{
    private readonly IHubContext<BoardsHub> _hubContext;

    public SignalRBoardRealtimeNotifier(IHubContext<BoardsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group(BoardHubGroups.ForBoard(mutation.BoardId))
            .SendAsync("boardMutation", mutation, cancellationToken);
    }
}
