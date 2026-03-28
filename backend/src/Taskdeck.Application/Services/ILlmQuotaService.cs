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
