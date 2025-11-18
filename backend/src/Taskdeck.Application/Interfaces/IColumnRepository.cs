using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IColumnRepository : IRepository<Column>
{
    Task<IEnumerable<Column>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default);
    Task<Column?> GetByIdWithCardsAsync(Guid id, CancellationToken cancellationToken = default);
}
