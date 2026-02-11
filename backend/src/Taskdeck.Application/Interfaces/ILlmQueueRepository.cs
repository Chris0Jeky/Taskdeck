using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

public interface ILlmQueueRepository : IRepository<LlmRequest>
{
    Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<LlmRequest>> GetByStatusAsync(RequestStatus status, CancellationToken cancellationToken = default);
    Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default);
}
