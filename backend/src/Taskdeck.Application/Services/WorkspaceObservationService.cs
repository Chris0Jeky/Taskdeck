using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>One explicit, evidence-bound model call. Shares the user's Chat quota and kill switch.</summary>
public sealed class WorkspaceObservationService(IWorkspaceObservationReader reader, IWorkspaceInsightRepository repository,
    ILlmProvider provider, ILlmQuotaService quota, ILlmKillSwitchService killSwitch)
{
    public async Task<Result<ObservationSourceDto>> SourceAsync(Guid userId, Guid boardId, Guid cardId, CancellationToken ct)
    {
        var source = await reader.SourceAsync(userId, boardId, cardId, ct);
        return source == null ? Missing<ObservationSourceDto>() : Result.Success(source);
    }

    public async Task<Result<List<QuietInsightDto>>> GenerateAsync(Guid userId, GenerateObservationsDto dto, CancellationToken ct)
    {
        var source = await reader.SourceAsync(userId, dto.BoardId, dto.CardId, ct);
        if (source == null) return Missing<List<QuietInsightDto>>();
        if (source.Fingerprint != dto.Fingerprint) return Changed();
        if (await killSwitch.IsKilledAsync(LlmSurface.Chat, userId, ct))
            return Result.Failure<List<QuietInsightDto>>(ErrorCodes.LlmKillSwitchActive, "Model analysis is disabled.");
        var health = await provider.GetHealthAsync(ct);
        if (health.IsMock || !health.IsAvailable)
            return Result.Failure<List<QuietInsightDto>>(ErrorCodes.InvalidOperation, "Model analysis needs an available configured provider. Structural analysis remains available.");

        var request = new ChatCompletionRequest([new("user", JsonSerializer.Serialize(source))], MaxTokens: 1600, Temperature: 0.2,
            Attribution: new(userId, Guid.NewGuid().ToString("N"), LlmRequestSourceSurface.Chat, dto.BoardId),
            SystemPrompt: WorkspaceObservationContract.Prompt);
        // UTF-8 byte count is a conservative input-token ceiling, plus bounded output and framing.
        var estimate = System.Text.Encoding.UTF8.GetByteCount(request.Messages[0].Content + WorkspaceObservationContract.Prompt) + 1800;
        var reservation = await quota.ReserveAsync(userId, LlmSurface.Chat, estimate, ct);
        if (!reservation.Allowed || reservation.ReservationId is not Guid reservationId)
            return Result.Failure<List<QuietInsightDto>>(ErrorCodes.LlmQuotaExceeded, reservation.DeniedReason ?? "Model budget is exhausted.");
        LlmCompletionResult? completion = null;
        try
        {
            // Re-read after quota admission too; do not dispatch stale or newly inaccessible evidence.
            var admitted = await reader.SourceAsync(userId, dto.BoardId, dto.CardId, ct);
            if (admitted == null) return Missing<List<QuietInsightDto>>();
            if (admitted.Fingerprint != source.Fingerprint) return Changed();
            completion = await provider.CompleteAsync(request, ct);
            if (completion.IsDegraded)
                return Result.Failure<List<QuietInsightDto>>(ErrorCodes.InvalidOperation, "The model could not complete this analysis. No observations were saved.");
            var candidates = WorkspaceObservationContract.Parse(completion.Content, source);
            if (candidates == null)
                return Result.Failure<List<QuietInsightDto>>(ErrorCodes.InvalidOperation, "The model returned unsupported or ungrounded observations. No observations were saved.");
            var current = await reader.SourceAsync(userId, dto.BoardId, dto.CardId, ct);
            if (current == null) return Missing<List<QuietInsightDto>>();
            if (current.Fingerprint != source.Fingerprint) return Changed();
            var items = await repository.InsightsAsync(userId, dto.BoardId, ct);
            var memories = await repository.MemoriesAsync(userId, dto.BoardId, ct);
            var now = DateTimeOffset.UtcNow;
            var result = new List<QuietInsightDto>();
            foreach (var candidate in candidates)
            {
                var rule = WorkspaceObservationContract.RulePrefix + candidate.Kind;
                var existing = items.SingleOrDefault(x => x.Rule == rule && x.TargetKey == dto.CardId.ToString());
                // Stable category + card identity collapses paraphrases and preserves user decisions.
                if (existing is { State: "dismissed" or "muted" or "snoozed" }) continue;
                if (existing != null && memories.Any(memory => memory.InsightId == existing.Id && !memory.Archived && memory.Status == "statement" &&
                    WorkspaceObservationContract.HasFingerprint(memory.OriginalEvidence, source.Fingerprint))) continue;
                var item = existing ?? new QuietInsight(userId, dto.BoardId, rule, dto.CardId.ToString(), dto.CardId, null);
                var evidence = JsonSerializer.Serialize(new ObservationEvidence(source.Fingerprint, candidate.Quote,
                    source.Title, now, completion.Provider, completion.Model));
                item.Refresh(candidate.Question, candidate.Reason, evidence, now);
                if (items.Any(x => x.Rule == rule && x.State == "muted")) item.Act("mute", now);
                if (existing == null) repository.Add(item);
                result.Add(new(item.Id, item.BoardId, item.CardId, null, item.Rule, item.Title, item.Detail, item.State,
                    item.Evidence, item.CheckedAt, item.SnoozeUntil));
            }
            return await repository.SaveAsync(ct) ? Result.Success(result) : Changed();
        }
        finally
        {
            // A cancelled/failed response can still have incurred upstream usage. Preserve the
            // existing dispatch-aware accounting contract rather than releasing billed work.
            var dispatch = request.DispatchContext.ReadSnapshot();
            var tokens = dispatch.Phase == LlmDispatchPhase.ObservedPreDispatch ? 0 :
                completion is { HasAuthoritativeTokenUsage: true, TokensUsed: > 0 } ? completion.TokensUsed :
                dispatch.Phase == LlmDispatchPhase.Dispatched || completion is { ShouldSettleQuotaReservation: true } ? reservation.EstimatedTokens : 0;
            if (tokens > 0)
                await quota.CommitReservationAsync(reservationId, userId, LlmSurface.Chat,
                    completion?.Provider ?? dispatch.Provider ?? health.ProviderName,
                    completion?.Model ?? dispatch.Model ?? health.Model ?? "", tokens, 0, CancellationToken.None);
            else await quota.ReleaseReservationAsync(reservationId, CancellationToken.None);
        }
    }

    private static Result<T> Missing<T>() => Result.Failure<T>(ErrorCodes.NotFound, "This observation source is unavailable.");
    private static Result<List<QuietInsightDto>> Changed() => Result.Failure<List<QuietInsightDto>>(ErrorCodes.Conflict,
        "The source changed. Preview current evidence before analyzing again.");
}
