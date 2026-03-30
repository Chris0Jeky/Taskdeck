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
}
