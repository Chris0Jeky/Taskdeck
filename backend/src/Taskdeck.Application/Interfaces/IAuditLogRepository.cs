using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

public interface IAuditLogRepository : IRepository<AuditLog>
{
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetByUserAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetByBoardAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default);
}
