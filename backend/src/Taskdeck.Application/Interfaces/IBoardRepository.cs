using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IBoardRepository : IRepository<Board>
{
    Task<IEnumerable<Board>> SearchAsync(string? searchText, bool includeArchived, CancellationToken cancellationToken = default);
    Task<Board?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
}
