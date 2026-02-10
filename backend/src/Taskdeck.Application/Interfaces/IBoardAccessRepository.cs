using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

public interface IBoardAccessRepository : IRepository<BoardAccess>
{
    Task<BoardAccess?> GetByBoardAndUserAsync(Guid boardId, Guid userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BoardAccess>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BoardAccess>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> HasAccessAsync(Guid boardId, Guid userId, UserRole? minimumRole = null, CancellationToken cancellationToken = default);
}
