namespace Taskdeck.Application.Services;

/// <summary>
/// Optional capability for notification sinks that can prepare durable work inside a caller-owned
/// transaction, then publish only the non-durable channel after that transaction commits.
/// </summary>
public interface ITransactionalBoardMutationNotifier
{
    Task StageBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default);

    Task NotifyCommittedBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default);
}
