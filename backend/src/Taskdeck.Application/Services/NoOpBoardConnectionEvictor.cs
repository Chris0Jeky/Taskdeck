namespace Taskdeck.Application.Services;

public sealed class NoOpBoardConnectionEvictor : IBoardConnectionEvictor
{
    public static readonly NoOpBoardConnectionEvictor Instance = new();

    private NoOpBoardConnectionEvictor()
    {
    }

    public Task EvictUserFromBoardAsync(
        Guid boardId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
