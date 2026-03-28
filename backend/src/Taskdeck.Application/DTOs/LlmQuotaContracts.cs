using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

public record QuotaCheckResultDto(
    bool Allowed,
    string? DeniedReason,
    long RemainingTokens,
    long RemainingRequests);

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
