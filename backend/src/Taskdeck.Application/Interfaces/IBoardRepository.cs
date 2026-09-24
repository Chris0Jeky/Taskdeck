using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IBoardRepository : IRepository<Board>
{
    Task<int> CountReadableByUserIdAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken = default);
    Task<int> CountReadableUpdatedSinceAsync(
        Guid userId,
        DateTimeOffset updatedSince,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the distinct people who can reach at least one board the given user can reach:
    /// the union of board owners and board-access grantees across every readable board,
    /// archived boards included. The caller is part of that union whenever they hold any board.
    /// Returns 0 only when the user can reach no board at all.
    /// Returns a count, never identities, so no other user is disclosed.
    /// </summary>
    Task<int> CountCollaborationMembersAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Board>> GetReadableByUserIdAsync(
        Guid userId,
        bool includeArchived,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<Board>> GetRecentReadableByUserIdAsync(
        Guid userId,
        int limit,
        bool includeArchived,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<Board>> SearchAsync(string? searchText, bool includeArchived, CancellationToken cancellationToken = default);
    Task<IEnumerable<Guid>> SearchIdsAsync(string? searchText, bool includeArchived, CancellationToken cancellationToken = default);
    Task<IEnumerable<Board>> GetByIdsAsync(IEnumerable<Guid> boardIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<Guid>> GetOwnedBoardIdsAsync(Guid userId, IEnumerable<Guid> candidateBoardIds, CancellationToken cancellationToken = default);
    /// <summary>Lists boards whose <c>OwnerId</c> is the given user (the board-creation ownership, distinct from Owner access rows).</summary>
    Task<IEnumerable<Board>> GetByOwnerIdAsync(Guid ownerId, bool includeArchived, CancellationToken cancellationToken = default);
    Task<Board?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads only the persisted archive flag without resolving through the change tracker.
    /// Returns <c>null</c> when the board no longer exists.
    /// </summary>
    Task<bool?> GetIsArchivedAsync(Guid id, CancellationToken cancellationToken = default);
}
