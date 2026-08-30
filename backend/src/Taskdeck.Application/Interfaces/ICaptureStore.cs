using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// The persistence façade for the durable <see cref="Capture"/> aggregate (ADR-0065 §Decision 1;
/// CF-01 <c>#2255</c>). Inbox reads move behind this contract once the ID-preserving backfill ships;
/// until then it only receives the dual-written mirrors of legacy queue captures. Writes are
/// staged into the ambient unit of work — callers persist through
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, so a capture mirror and its queue row commit together.
/// </summary>
public interface ICaptureStore
{
    Task AddAsync(Capture capture, CancellationToken cancellationToken = default);

    Task<Capture?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
