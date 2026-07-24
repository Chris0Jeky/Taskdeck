using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

public interface ILlmUsageRecordRepository : IRepository<LlmUsageRecord>
{
    /// <summary>
    /// Atomically checks quota and, if room remains, inserts a reservation row (issue #1313). On SQLite
    /// the check and insert are one conditional <c>INSERT ... SELECT ... WHERE</c> statement executed
    /// under the database's single-writer lock — no explicit transaction — so two concurrent
    /// boundary-crossers serialize and the second's limit subqueries observe the first's row. Expired
    /// reservations are swept first, then request/token limits are evaluated against live
    /// (committed + non-expired reserved) usage. Limits of 0 mean unlimited.
    /// </summary>
    Task<QuotaReservationOutcome> TryReserveAsync(
        Guid userId,
        LlmSurface surface,
        DateTimeOffset hourStart,
        DateTimeOffset now,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        long requestsPerHour,
        long tokensPerDay,
        long globalBudgetCeilingTokens,
        int estimatedTokens,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes a reservation with the actual token counts (Reserved → Committed). If the reservation
    /// row is gone (TTL-swept during a slow LLM call), a replacement Committed row is inserted with the
    /// same id, userId and surface so real billed usage is never dropped from quota/telemetry
    /// (<see cref="QuotaCommitResult.RecoveredExpired"/>). Idempotent: a duplicate commit for an
    /// already-settled id changes nothing (<see cref="QuotaCommitResult.AlreadySettled"/>).
    /// </summary>
    Task<QuotaCommitResult> CommitReservationAsync(
        Guid reservationId,
        Guid userId,
        LlmSurface surface,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases (deletes) a still-reserved row so it consumes no quota. Returns false if no live
    /// reservation with that id exists. Committed rows are never touched.
    /// </summary>
    Task<bool> ReleaseReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken = default);

    Task<long> GetRequestCountAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<long> GetTotalTokensAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<(long TotalInputTokens, long TotalOutputTokens, long TotalRequests)> GetUsageSummaryAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
