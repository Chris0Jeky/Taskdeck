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

    public LlmQuotaService(IUnitOfWork unitOfWork, LlmQuotaSettings settings)
    {
        _unitOfWork = unitOfWork;
        _settings = settings;
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

        // Check per-user requests per hour
        if (_settings.RequestsPerHour > 0)
        {
            var hourStart = now.AddHours(-1);
            var requestCount = await _unitOfWork.LlmUsageRecords.GetRequestCountAsync(
                userId, null, hourStart, now, ct);

            if (requestCount >= _settings.RequestsPerHour)
            {
                return new QuotaCheckResultDto(
                    Allowed: false,
                    DeniedReason: $"Per-user hourly request limit ({_settings.RequestsPerHour}) exceeded",
                    RemainingTokens: 0,
                    RemainingRequests: 0);
            }
        }

        // Check per-user tokens per day
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        long remainingTokens = _settings.TokensPerDay > 0 ? _settings.TokensPerDay : long.MaxValue;

        if (_settings.TokensPerDay > 0)
        {
            var tokensUsed = await _unitOfWork.LlmUsageRecords.GetTotalTokensAsync(
                userId, null, dayStart, dayEnd, ct);

            if (tokensUsed >= _settings.TokensPerDay)
            {
                return new QuotaCheckResultDto(
                    Allowed: false,
                    DeniedReason: $"Per-user daily token budget ({_settings.TokensPerDay}) exhausted",
                    RemainingTokens: 0,
                    RemainingRequests: 0);
            }

            remainingTokens = _settings.TokensPerDay - tokensUsed;
        }

        // Check global budget ceiling
        if (_settings.GlobalBudgetCeilingTokens > 0)
        {
            var globalTokensUsed = await _unitOfWork.LlmUsageRecords.GetTotalTokensAsync(
                null, null, dayStart, dayEnd, ct);

            if (globalTokensUsed >= _settings.GlobalBudgetCeilingTokens)
            {
                return new QuotaCheckResultDto(
                    Allowed: false,
                    DeniedReason: "Global daily token budget exhausted",
                    RemainingTokens: 0,
                    RemainingRequests: 0);
            }

            var globalRemaining = _settings.GlobalBudgetCeilingTokens - globalTokensUsed;
            remainingTokens = Math.Min(remainingTokens, globalRemaining);
        }

        long remainingRequests = _settings.RequestsPerHour > 0
            ? _settings.RequestsPerHour - await _unitOfWork.LlmUsageRecords.GetRequestCountAsync(
                userId, null, now.AddHours(-1), now, ct)
            : long.MaxValue;

        return new QuotaCheckResultDto(
            Allowed: true,
            DeniedReason: null,
            RemainingTokens: remainingTokens,
            RemainingRequests: Math.Max(0, remainingRequests));
    }

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
