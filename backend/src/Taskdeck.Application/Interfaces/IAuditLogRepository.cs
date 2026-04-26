using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Lightweight projection record for per-day audit entry counts.
/// Used by <see cref="IAuditLogRepository.CountByDateAsync"/> to avoid loading full entities.
/// </summary>
public sealed record DailyAuditCount(DateOnly Date, int Count);

public interface IAuditLogRepository : IRepository<AuditLog>
{
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetByUserAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetByBoardAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> QueryAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? userId = null,
        Guid? boardId = null,
        string? source = null,
        string? level = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns per-day entry counts for a user within a time range.
    /// Uses a server-side GROUP BY projection to avoid loading full entities into memory.
    /// </summary>
    /// <param name="from">Start of the time range (inclusive).</param>
    /// <param name="to">End of the time range (inclusive).</param>
    /// <param name="userId">The user whose entries to count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of (Date, Count) pairs for days with at least one entry.</returns>
    Task<IReadOnlyList<DailyAuditCount>> CountByDateAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes audit log entries older than the specified cutoff date in batches.
    /// Uses direct SQL DELETE for efficiency (does not load entities into memory).
    /// </summary>
    /// <param name="olderThan">Entries with a Timestamp before this value are deleted.</param>
    /// <param name="batchSize">Maximum number of rows to delete per batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total number of rows deleted.</returns>
    Task<int> DeleteOldEntriesAsync(DateTimeOffset olderThan, int batchSize, CancellationToken cancellationToken = default);
}
