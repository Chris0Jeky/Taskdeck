using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IArchiveItemRepository : IRepository<ArchiveItem>
{
    Task<IEnumerable<ArchiveItem>> GetPageAsync(int limit = 100, CancellationToken cancellationToken = default, int offset = 0);
    Task<IEnumerable<ArchiveItem>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default, int offset = 0);
    Task<IEnumerable<ArchiveItem>> GetByEntityTypeAsync(string entityType, int limit = 100, CancellationToken cancellationToken = default, int offset = 0);
    Task<IEnumerable<ArchiveItem>> GetByStatusAsync(RestoreStatus status, int limit = 100, CancellationToken cancellationToken = default, int offset = 0);
    Task<ArchiveItem?> GetByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ArchiveItem>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default);
}
