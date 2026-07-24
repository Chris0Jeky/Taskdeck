using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

public interface ILlmQuotaService
{
    Task RecordUsageAsync(
        Guid userId,
        LlmSurface surface,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        CancellationToken ct = default);

    Task<QuotaCheckResultDto> CheckQuotaAsync(
        Guid userId,
        LlmSurface surface,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically reserves quota for one LLM call (issue #1313): the limit check and the reservation
    /// insert execute as one conditional statement under the database's single-writer serialization, so
    /// concurrent callers cannot both pass at the boundary. On success the caller MUST later
    /// <see cref="CommitReservationAsync"/> it with the actual token counts, or
    /// <see cref="ReleaseReservationAsync"/> it if no LLM usage occurred / the call failed. A denial
    /// carries the same <c>DeniedReason</c> a <see cref="CheckQuotaAsync"/> denial would, preserving the
    /// HTTP status/error contract.
    /// </summary>
    Task<QuotaReservationDto> ReserveAsync(
        Guid userId,
        LlmSurface surface,
        CancellationToken ct = default);

    /// <summary>
    /// Finalizes a reservation with the actual token counts (Reserved → Committed usage). userId and
    /// surface are required so billed usage can be recovered into a fresh committed row if the
    /// reservation was TTL-swept during a slow LLM call — real usage is never silently dropped.
    /// </summary>
    Task CommitReservationAsync(
        Guid reservationId,
        Guid userId,
        LlmSurface surface,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        CancellationToken ct = default);

    /// <summary>Releases a reservation so it consumes no quota (no LLM usage occurred or the call failed).</summary>
    Task ReleaseReservationAsync(
        Guid reservationId,
        CancellationToken ct = default);

    Task<UsageSummaryDto> GetUsageSummaryAsync(
        Guid? userId = null,
        LlmSurface? surface = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    Task<QuotaStatusDto> GetQuotaStatusAsync(
        Guid userId,
        CancellationToken ct = default);
}
