using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class LlmQuotaService : ILlmQuotaService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly LlmQuotaSettings _settings;
    private readonly ILogger<LlmQuotaService>? _logger;

    public LlmQuotaService(
        IUnitOfWork unitOfWork,
        LlmQuotaSettings settings,
        ILogger<LlmQuotaService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _settings = settings;
        _logger = logger;
    }

    public async Task RecordUsageAsync(
        Guid userId,
        LlmSurface surface,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        CancellationToken ct = default)
    {
        var record = new LlmUsageRecord(userId, surface, provider, model, inputTokens, outputTokens);
        await _unitOfWork.LlmUsageRecords.AddAsync(record, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<QuotaCheckResultDto> CheckQuotaAsync(
        Guid userId,
        LlmSurface surface,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var hourStart = now.AddHours(-1);
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        // Fetch request count once and reuse for both the limit check and remaining calculation
        long requestCount = _settings.RequestsPerHour > 0
            ? await _unitOfWork.LlmUsageRecords.GetRequestCountAsync(userId, surface, hourStart, now, ct)
            : 0;

        // Check per-user requests per hour
        if (_settings.RequestsPerHour > 0 && requestCount >= _settings.RequestsPerHour)
        {
            return new QuotaCheckResultDto(
                Allowed: false,
                DeniedReason: RequestsExceededReason(_settings.RequestsPerHour),
                RemainingTokens: 0,
                RemainingRequests: 0);
        }

        // Check per-user tokens per day
        long remainingTokens = _settings.TokensPerDay > 0 ? _settings.TokensPerDay : long.MaxValue;

        if (_settings.TokensPerDay > 0)
        {
            var tokensUsed = await _unitOfWork.LlmUsageRecords.GetTotalTokensAsync(
                userId, surface, dayStart, dayEnd, ct);

            if (tokensUsed >= _settings.TokensPerDay)
            {
                return new QuotaCheckResultDto(
                    Allowed: false,
                    DeniedReason: TokensExceededReason(_settings.TokensPerDay),
                    RemainingTokens: 0,
                    RemainingRequests: 0);
            }

            remainingTokens = _settings.TokensPerDay - tokensUsed;
        }

        // Check global budget ceiling
        if (_settings.GlobalBudgetCeilingTokens > 0)
        {
            var globalTokensUsed = await _unitOfWork.LlmUsageRecords.GetTotalTokensAsync(
                null, surface, dayStart, dayEnd, ct);

            if (globalTokensUsed >= _settings.GlobalBudgetCeilingTokens)
            {
                return new QuotaCheckResultDto(
                    Allowed: false,
                    DeniedReason: GlobalExceededReason,
                    RemainingTokens: 0,
                    RemainingRequests: 0);
            }

            var globalRemaining = _settings.GlobalBudgetCeilingTokens - globalTokensUsed;
            remainingTokens = Math.Min(remainingTokens, globalRemaining);
        }

        long remainingRequests = _settings.RequestsPerHour > 0
            ? _settings.RequestsPerHour - requestCount
            : long.MaxValue;

        return new QuotaCheckResultDto(
            Allowed: true,
            DeniedReason: null,
            RemainingTokens: remainingTokens,
            RemainingRequests: Math.Max(0, remainingRequests));
    }

    public async Task<QuotaReservationDto> ReserveAsync(
        Guid userId,
        LlmSurface surface,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var hourStart = now.AddHours(-1);
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var expiresAt = now.AddSeconds(Math.Max(1, _settings.ReservationTtlSeconds));
        // Floor of 1: an estimate of 0 would make reserved rows contribute nothing to the token/global
        // SUM subqueries, silently reopening the concurrent token-budget TOCTOU this fix closes.
        var estimatedTokens = Math.Max(1, _settings.ReservationEstimatedTokens);

        var outcome = await _unitOfWork.LlmUsageRecords.TryReserveAsync(
            userId,
            surface,
            hourStart,
            now,
            dayStart,
            dayEnd,
            _settings.RequestsPerHour,
            _settings.TokensPerDay,
            _settings.GlobalBudgetCeilingTokens,
            estimatedTokens,
            expiresAt,
            ct);

        switch (outcome.Decision)
        {
            case QuotaReservationDecision.RequestsExceeded:
                return Denied(RequestsExceededReason(_settings.RequestsPerHour));
            case QuotaReservationDecision.TokensExceeded:
                return Denied(TokensExceededReason(_settings.TokensPerDay));
            case QuotaReservationDecision.GlobalExceeded:
                return Denied(GlobalExceededReason);
        }

        // Allowed: the just-inserted reservation is included in outcome.RequestCount/UserTokens so the
        // remaining headroom already accounts for this call.
        long remainingRequests = _settings.RequestsPerHour > 0
            ? Math.Max(0, _settings.RequestsPerHour - outcome.RequestCount)
            : long.MaxValue;

        long remainingTokens = _settings.TokensPerDay > 0
            ? Math.Max(0, _settings.TokensPerDay - outcome.UserTokens)
            : long.MaxValue;

        if (_settings.GlobalBudgetCeilingTokens > 0)
        {
            var globalRemaining = Math.Max(0, _settings.GlobalBudgetCeilingTokens - outcome.GlobalTokens);
            remainingTokens = Math.Min(remainingTokens, globalRemaining);
        }

        return new QuotaReservationDto(
            Allowed: true,
            DeniedReason: null,
            ReservationId: outcome.ReservationId,
            RemainingTokens: remainingTokens,
            RemainingRequests: remainingRequests,
            EstimatedTokens: estimatedTokens);

        static QuotaReservationDto Denied(string reason) =>
            new(Allowed: false, DeniedReason: reason, ReservationId: null, RemainingTokens: 0, RemainingRequests: 0);
    }

    public async Task CommitReservationAsync(
        Guid reservationId,
        Guid userId,
        LlmSurface surface,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        CancellationToken ct = default)
    {
        var result = await _unitOfWork.LlmUsageRecords.CommitReservationAsync(
            reservationId, userId, surface, provider, model, inputTokens, outputTokens, ct);

        if (result == QuotaCommitResult.RecoveredExpired)
        {
            // The LLM call outlived ReservationTtlSeconds and the reservation was swept mid-call; the
            // repository inserted a replacement committed row so the billed tokens still count. Surfaced
            // as a warning because recurring sweeps mean the TTL is too short for real call latency.
            _logger?.LogWarning(
                "Quota reservation {ReservationId} (user {UserId}, surface {Surface}) expired mid-call " +
                "after {TtlSeconds}s; {Tokens} billed tokens recovered into a fresh committed usage row. " +
                "Consider raising LlmQuota:ReservationTtlSeconds if this recurs.",
                reservationId,
                userId,
                surface,
                _settings.ReservationTtlSeconds,
                inputTokens + outputTokens);
        }
    }

    public Task ReleaseReservationAsync(Guid reservationId, CancellationToken ct = default)
    {
        return _unitOfWork.LlmUsageRecords.ReleaseReservationAsync(reservationId, ct);
    }

    private static string RequestsExceededReason(long requestsPerHour) =>
        $"Per-user hourly request limit ({requestsPerHour}) exceeded";

    private static string TokensExceededReason(long tokensPerDay) =>
        $"Per-user daily token budget ({tokensPerDay}) exhausted";

    private const string GlobalExceededReason = "Global daily token budget exhausted";

    public async Task<UsageSummaryDto> GetUsageSummaryAsync(
        Guid? userId = null,
        LlmSurface? surface = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var windowStart = from ?? new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var windowEnd = to ?? DateTimeOffset.UtcNow;

        var (totalInput, totalOutput, totalRequests) = await _unitOfWork.LlmUsageRecords
            .GetUsageSummaryAsync(userId, surface, windowStart, windowEnd, ct);

        return new UsageSummaryDto(
            userId,
            surface,
            totalRequests,
            totalInput,
            totalOutput,
            totalInput + totalOutput,
            windowStart,
            windowEnd);
    }

    public async Task<QuotaStatusDto> GetQuotaStatusAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var hourStart = now.AddHours(-1);

        var tokensUsedToday = await _unitOfWork.LlmUsageRecords.GetTotalTokensAsync(
            userId, null, dayStart, dayEnd, ct);
        var requestsThisHour = await _unitOfWork.LlmUsageRecords.GetRequestCountAsync(
            userId, null, hourStart, now, ct);

        var quotaCheck = await CheckQuotaAsync(userId, LlmSurface.Chat, ct);

        return new QuotaStatusDto(
            Allowed: quotaCheck.Allowed,
            TokensUsedToday: tokensUsedToday,
            TokenBudgetCeiling: _settings.TokensPerDay,
            RequestsThisHour: requestsThisHour,
            RequestsPerHourLimit: _settings.RequestsPerHour);
    }
}
