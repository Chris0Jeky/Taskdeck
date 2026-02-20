namespace Taskdeck.Application.Services;

public sealed class NoOpBoardRealtimeNotifier : IBoardRealtimeNotifier
{
    public static readonly NoOpBoardRealtimeNotifier Instance = new();

    private NoOpBoardRealtimeNotifier()
    {
    }

    public Task NotifyBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
