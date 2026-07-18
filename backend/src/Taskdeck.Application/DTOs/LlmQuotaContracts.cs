using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

public record QuotaCheckResultDto(
    bool Allowed,
    string? DeniedReason,
    long RemainingTokens,
    long RemainingRequests);

/// <summary>
/// Result of an atomic quota reservation (issue #1313). When <see cref="Allowed"/> is true a
/// <see cref="ReservationId"/> is returned that must later be committed (with actual token counts) or
/// released. When false, <see cref="DeniedReason"/> carries the same message a synchronous quota check
/// would produce so the HTTP surface maps it identically.
/// </summary>
public record QuotaReservationDto(
    bool Allowed,
    string? DeniedReason,
    Guid? ReservationId,
    long RemainingTokens,
    long RemainingRequests);

/// <summary>Outcome of finalizing a quota reservation with actual token counts.</summary>
public enum QuotaCommitResult
{
    /// <summary>The live reservation row was updated to committed usage (the normal path).</summary>
    Committed,

    /// <summary>
    /// The reservation was gone at commit time (TTL-swept during a slow LLM call); a replacement
    /// committed usage row was inserted so the billed tokens still count against quota and telemetry.
    /// </summary>
    RecoveredExpired,

    /// <summary>A row with this id already exists in a settled state (idempotent duplicate commit).</summary>
    AlreadySettled
}

/// <summary>Which quota limit (if any) an atomic reservation attempt hit.</summary>
public enum QuotaReservationDecision
{
    Allowed,
    RequestsExceeded,
    TokensExceeded,
    GlobalExceeded
}

/// <summary>
/// Low-level outcome of the repository's atomic reserve operation: the decision plus the live
/// (committed + non-expired reserved) counts observed inside the serialized transaction, so the
/// service can compute remaining headroom without a second, racy read.
/// </summary>
public readonly record struct QuotaReservationOutcome(
    QuotaReservationDecision Decision,
    Guid? ReservationId,
    long RequestCount,
    long UserTokens,
    long GlobalTokens);

public record UsageSummaryDto(
    Guid? UserId,
    LlmSurface? Surface,
    long TotalRequests,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalTokens,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd);

public record KillSwitchEntryDto(
    KillSwitchScope Scope,
    string? Target,
    bool Enabled,
    string? Reason);

public record KillSwitchStatusDto(
    bool GlobalKilled,
    IReadOnlyList<KillSwitchEntryDto> Entries);

public record SetKillSwitchRequestDto(
    KillSwitchScope Scope,
    string? Target,
    bool Enabled,
    string? Reason);

public record QuotaStatusDto(
    bool Allowed,
    long TokensUsedToday,
    long TokenBudgetCeiling,
    long RequestsThisHour,
    long RequestsPerHourLimit);
