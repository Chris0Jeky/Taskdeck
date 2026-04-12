using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface ICardRepository : IRepository<Card>
{
    Task<IEnumerable<Card>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Card>> GetByBoardIdsAsync(IEnumerable<Guid> boardIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<Card>> GetAgendaByBoardIdsAsync(IEnumerable<Guid> boardIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<Card>> GetByColumnIdAsync(Guid columnId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Card>> SearchAsync(Guid boardId, string? searchText, Guid? labelId, Guid? columnId, CancellationToken cancellationToken = default);
    Task<Card?> GetByIdWithLabelsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Card>> SearchAcrossBoardsAsync(IEnumerable<Guid> boardIds, string searchText, int maxResults, CancellationToken cancellationToken = default);
    Task<IEnumerable<Card>> SearchAcrossBoardsAsync(IEnumerable<Guid> boardIds, string searchText, int maxResults, int offset, CancellationToken cancellationToken = default);
    Task<int> CountSearchAcrossBoardsAsync(IEnumerable<Guid> boardIds, string searchText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight query for board metrics: returns cards without eager-loading labels.
    /// Optionally filters to a specific label (via SQL join), and/or to specific card IDs.
    /// </summary>
    Task<IEnumerable<Card>> GetForMetricsAsync(
        Guid boardId,
        Guid? labelId = null,
        IEnumerable<Guid>? cardIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Count cards per column for a board at SQL level, avoiding loading card entities.
    /// Returns (columnId, cardCount) pairs.
    /// </summary>
    Task<IReadOnlyList<(Guid ColumnId, int CardCount)>> CountCardsByColumnAsync(
        Guid boardId,
        Guid? labelId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get only blocked cards for a board at SQL level, without eager-loading labels.
    /// Optionally filters by label.
    /// </summary>
    Task<IEnumerable<Card>> GetBlockedByBoardIdAsync(
        Guid boardId,
        Guid? labelId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cards with due dates falling within the specified date range across multiple boards.
    /// Returns cards ordered by due date ascending.
    /// </summary>
    Task<IEnumerable<Card>> GetByDueDateRangeAsync(
        IEnumerable<Guid> boardIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
