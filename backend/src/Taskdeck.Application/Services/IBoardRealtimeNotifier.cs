namespace Taskdeck.Application.Services;

public interface IBoardRealtimeNotifier
{
    Task NotifyBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default);
}
