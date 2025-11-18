using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface ILabelRepository : IRepository<Label>
{
    Task<IEnumerable<Label>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default);
}
