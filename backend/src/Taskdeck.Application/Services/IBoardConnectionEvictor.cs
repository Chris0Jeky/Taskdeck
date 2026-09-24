namespace Taskdeck.Application.Services;

/// <summary>
/// Removes a user's live realtime connections from a board group (#3420/#3407).
/// Implemented in the Api layer where the SignalR hub context lives; services
/// call it after access revocation commits so revoked members stop receiving
/// board broadcasts immediately instead of at disconnect.
/// </summary>
public interface IBoardConnectionEvictor
{
    Task EvictUserFromBoardAsync(
        Guid boardId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
