using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IArchiveItemRepository : IRepository<ArchiveItem>
{
    Task<IEnumerable<ArchiveItem>> GetByBoardIdAsync(string boardId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ArchiveItem>> GetByEntityTypeAsync(string entityType, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ArchiveItem>> GetByStatusAsync(RestoreStatus status, int limit = 100, CancellationToken cancellationToken = default);
    Task<ArchiveItem?> GetByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ArchiveItem>> GetByUserIdAsync(string userId, int limit = 100, CancellationToken cancellationToken = default);
}
