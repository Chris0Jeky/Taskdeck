using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// LLM-backed transcript task extraction (REVIVAL-08 M1). Runs the guardrail chain the chat surface
/// established (kill switch ? provider health ? quota ? completion ? usage recording), then parses
/// and sanitizes the completion into a contract-valid <see cref="CaptureTriageOutputV1"/>.
/// Every failure mode is returned as an outcome, never thrown, so <see cref="CaptureTriageService"/>
/// can degrade to the deterministic extractor. Provider/model are reported only on success (#1273).
/// </summary>
public class LlmCaptureTriageExtractor : ILlmCaptureTriageExtractor
{
    private readonly ILlmProvider _llmProvider;
    private readonly LlmCaptureTriageSettings _settings;
    private readonly ILlmKillSwitchService? _killSwitchService;
    private readonly ILlmQuotaService? _quotaService;
    private readonly ILogger<LlmCaptureTriageExtractor>? _logger;
    private readonly ILlmCaptureTriageProgressReporter? _progressReporter;

    public LlmCaptureTriageExtractor(
        ILlmProvider llmProvider,
        LlmCaptureTriageSettings settings,
        ILlmKillSwitchService? killSwitchService = null,
        ILlmQuotaService? quotaService = null,
        ILogger<LlmCaptureTriageExtractor>? logger = null,
        ILlmCaptureTriageProgressReporter? progressReporter = null)
    {
        _llmProvider = llmProvider;
        _settings = settings;
        _killSwitchService = killSwitchService;
        _quotaService = quotaService;
        _logger = logger;
        _progressReporter = progressReporter;
    }

    public async Task<LlmCaptureTriageExtraction> ExtractAsync(
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken = default)
    {
        var chunks = TranscriptTriageChunker.Chunk(
            payload.Text,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens);

        if (chunks.Count <= 1)
        {
            return await ExtractChunkAsync(userId, boardId, payload, cancellationToken);
        }

        if (chunks.Count > _settings.MaxChunkCount)
        {
            _logger?.LogWarning(
                "Transcript triage requires {ChunkCount} map chunks, exceeding configured maximum {MaxChunkCount}; using deterministic fallback without provider calls.",
                chunks.Count,
                _settings.MaxChunkCount);
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.InvalidOutput,
                Detail: "Transcript exceeds the configured map-chunk call budget.");
        }

        var mappedTasks = new List<IReadOnlyList<CaptureTriageTaskV1>>(chunks.Count);
        string? provider = null;
        string? model = null;

        foreach (var chunk in chunks)
        {
            var chunkPayload = payload with { Text = chunk.Text };
            var extraction = await ExtractChunkAsync(userId, boardId, chunkPayload, cancellationToken);

            if (extraction.Succeeded)
            {
                if (!TrySetProviderIdentity(extraction, ref provider, ref model))
                {
                    return new LlmCaptureTriageExtraction(
                        LlmCaptureTriageOutcome.InvalidOutput,
                        Detail: "Chunked transcript extraction returned inconsistent provider provenance.");
                }

                mappedTasks.Add(extraction.Output!.Tasks);
                continue;
            }

            if (extraction.Outcome == LlmCaptureTriageOutcome.EmptyExtraction)
            {
                if (!TrySetProviderIdentity(extraction, ref provider, ref model))
                {
                    return new LlmCaptureTriageExtraction(
                        LlmCaptureTriageOutcome.InvalidOutput,
                        Detail: "Chunked transcript extraction returned inconsistent provider provenance.");
                }

                continue;
            }

            // Fail safe: a proposal based on only successful map legs would silently omit the failed
            // transcript range. Discard every mapped result and let CaptureTriageService take its M1
            // deterministic fallback, which retains its existing honest provenance behavior.
            _logger?.LogWarning(
                "Chunked transcript triage failed at chunk {ChunkIndex}; discarding {MappedTaskCount} mapped tasks for deterministic fallback. Outcome: {Outcome}",
                chunk.Index,
                mappedTasks.Sum(tasks => tasks.Count),
                extraction.Outcome);
            return new LlmCaptureTriageExtraction(extraction.Outcome, Detail: extraction.Detail);
        }

        if (mappedTasks.Count == 0)
        {
            // Every chunk returned the model's explicit no-action verdict. This remains a successful
            // no-proposal terminal state, rather than falling back to the deterministic whole-text
            // extractor (which would fabricate one card from unactionable transcript text).
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.EmptyExtraction,
                Provider: provider,
                Model: model);
        }

        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionLlmV1,
            ReduceMappedTasks(mappedTasks));
        var validation = CaptureTriageOutputContract.Validate(output);
        if (!validation.IsSuccess)
        {
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.InvalidOutput,
                Detail: validation.ErrorMessage);
        }

        return new LlmCaptureTriageExtraction(
            LlmCaptureTriageOutcome.Succeeded,
            validation.Value,
            provider,
            model);
    }

    private async Task<LlmCaptureTriageExtraction> ExtractChunkAsync(
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_settings.Enabled)
        {
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.Disabled);
        }

        if (_killSwitchService is not null &&
            await _killSwitchService.IsKilledAsync(LlmSurface.CaptureTriage, userId, cancellationToken))
        {
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.KillSwitchActive);
        }

        // GetHealthAsync is a passive config check on every provider (no network call), so gating
        // each triage on it is cheap. IsMock is the "a live provider is configured" test from the
        // REVIVAL-08 brief: the mock's canned chat output can never satisfy the triage contract, so
        // calling it would only burn a guaranteed fallback.
        var health = await _llmProvider.GetHealthAsync(cancellationToken);
        if (health.IsMock)
        {
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.ProviderIsMock);
        }

        if (!health.IsAvailable)
        {
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.ProviderUnavailable,
                Detail: health.ErrorMessage);
        }

        // Atomic quota reservation (issue #1313): reserve before the completion, then commit with the
        // actual tokens or release. This serializes concurrent triage/chat calls at the boundary.
        Guid? quotaReservationId = null;
        if (_quotaService is not null)
        {
            var reservationEstimate = EstimateReservationTokens(payload.Text);
            var reservation = await _quotaService.ReserveAsync(
                userId,
                LlmSurface.CaptureTriage,
                reservationEstimate,
                cancellationToken);
            if (!reservation.Allowed)
            {
                return new LlmCaptureTriageExtraction(
                    LlmCaptureTriageOutcome.QuotaExceeded,
                    Detail: reservation.DeniedReason);
            }
            quotaReservationId = reservation.ReservationId;
        }

        var request = new ChatCompletionRequest(
            Messages: [new ChatCompletionMessage("user", payload.Text)],
            MaxTokens: _settings.MaxOutputTokens,
            Temperature: _settings.Temperature,
            Attribution: new LlmRequestAttribution(
                userId,
                LlmRequestAttributionMapper.ResolveCorrelationIdFromActivity(),
                LlmRequestSourceSurface.Capture,
                boardId),
            // A non-null SystemPrompt opts out of the providers' chat instruction-extraction mode;
            // the prompt itself demands raw JSON and TryParseTasks tolerates fenced output.
            SystemPrompt: LlmCaptureTriagePrompt.SystemPrompt);

        LlmCompletionResult result;
        LlmCompletionResult? completed = null;
        var quotaSettled = quotaReservationId is null;
        try
        {
            // A map-reduce run can span many legal provider calls. Pulse at each call boundary so
            // the worker-health budget covers one configured provider timeout, rather than the
            // whole bounded run. This is best-effort telemetry; it must never affect triage.
            ReportProviderProgress();
            result = await _llmProvider.CompleteAsync(request, cancellationToken);
            completed = result;

            // Settle the reservation. Tokens are consumed whether or not the content is usable
            // (truncation is the clearest case: degraded AND billed), so a call that used tokens commits
            // before any quality checks; a zero-token call releases (mirrors the prior "record only if
            // > 0" behavior).
            if (quotaReservationId is Guid reservationId)
            {
                if (result.TokensUsed > 0)
                {
                    // CancellationToken.None (M1, #1427 review): tokens are already billed at this
                    // point, so finalization must not be cancellable — a cancelled commit would trip
                    // the finally-release below and erase real usage (quota bypass).
                    await _quotaService!.CommitReservationAsync(
                        reservationId,
                        userId,
                        LlmSurface.CaptureTriage,
                        result.Provider,
                        result.Model,
                        result.TokensUsed,
                        0,
                        CancellationToken.None);
                }
                else
                {
                    await _quotaService!.ReleaseReservationAsync(reservationId, CancellationToken.None);
                }
                quotaSettled = true;
            }
        }
        finally
        {
            // Covers success, provider failure, and cancellation after the provider boundary.
            ReportProviderProgress();

            // Mirrors ChatService (#1427 review): SETTLE an unfinalized reservation. If the provider
            // completed with billed tokens (the in-try commit itself failed on a DB fault), COMMIT them —
            // releasing would erase real usage; a provider throw or zero-token result releases so the
            // slot consumes no quota. CancellationToken.None so cleanup runs under a cancelled request
            // token; try/catch so a settle failure cannot mask the original exception.
            if (!quotaSettled && quotaReservationId is Guid unsettledId)
            {
                try
                {
                    if (completed is { TokensUsed: > 0 } billed)
                    {
                        await _quotaService!.CommitReservationAsync(
                            unsettledId,
                            userId,
                            LlmSurface.CaptureTriage,
                            billed.Provider,
                            billed.Model,
                            billed.TokensUsed,
                            0,
                            CancellationToken.None);
                    }
                    else
                    {
                        await _quotaService!.ReleaseReservationAsync(unsettledId, CancellationToken.None);
                    }
                }
                catch (Exception settleEx)
                {
                    _logger?.LogError(
                        settleEx,
                        "Quota reservation {ReservationId} settle failed in transcript triage (billed tokens: {Tokens}); " +
                        "the row stays Reserved until the TTL sweep.",
                        unsettledId,
                        completed?.TokensUsed ?? 0);
                }
            }
        }

        if (result.IsDegraded)
        {
            _logger?.LogWarning(
                "LLM transcript triage degraded for user {UserId}: {DegradedReason}",
                userId,
                result.DegradedReason ?? "unknown");
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.ProviderDegraded,
                Detail: result.DegradedReason);
        }

        if (!LlmCaptureTriagePrompt.TryParseTasks(result.Content, out var rawTasks))
        {
            _logger?.LogWarning(
                "LLM transcript triage returned unparseable output for user {UserId} (provider {Provider}, {ContentLength} chars)",
                userId,
                result.Provider,
                result.Content?.Length ?? 0);
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.InvalidOutput);
        }

        if (rawTasks.Count == 0)
        {
            // The LLM ran and deliberately reported zero action items — a real verdict, so the
            // provider/model that produced it are reported for honest provenance stamping.
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.EmptyExtraction,
                Provider: result.Provider,
                Model: result.Model);
        }

        var sanitized = SanitizeTasks(rawTasks);
        if (sanitized.Count == 0)
        {
            // The model returned entries but none survived sanitization — treat as malformed
            // output (fallback), not as a deliberate empty verdict.
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.InvalidOutput);
        }

        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionLlmV1,
            sanitized);

        var validation = CaptureTriageOutputContract.Validate(output);
        if (!validation.IsSuccess)
        {
            _logger?.LogWarning(
                "LLM transcript triage output failed contract validation for user {UserId}: {Error}",
                userId,
                validation.ErrorMessage);
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.InvalidOutput);
        }

        return new LlmCaptureTriageExtraction(
            LlmCaptureTriageOutcome.Succeeded,
            validation.Value,
            result.Provider,
            result.Model);
    }

    private void ReportProviderProgress()
    {
        try
        {
            _progressReporter?.ReportProgress();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Transcript triage progress reporting failed; continuing extraction");
        }
    }

    private static bool TrySetProviderIdentity(
        LlmCaptureTriageExtraction extraction,
        ref string? provider,
        ref string? model)
    {
        if (string.IsNullOrWhiteSpace(extraction.Provider) || string.IsNullOrWhiteSpace(extraction.Model))
        {
            return false;
        }

        if (provider is null && model is null)
        {
            provider = extraction.Provider;
            model = extraction.Model;
            return true;
        }

        return string.Equals(provider, extraction.Provider, StringComparison.Ordinal) &&
               string.Equals(model, extraction.Model, StringComparison.Ordinal);
    }

    private int EstimateReservationTokens(string text)
    {
        // The provider receives both the extraction system prompt and this map chunk. Reserve the
        // whole bounded request plus its configured output allowance before the call so concurrent
        // long-transcript triage cannot reopen the quota-budget race fixed by #1313.
        var estimate = (long)TranscriptTokenEstimator.EstimateTokens(LlmCaptureTriagePrompt.SystemPrompt) +
                       TranscriptTokenEstimator.EstimateTokens(text) +
                       _settings.MaxOutputTokens;
        return (int)Math.Clamp(estimate, 1L, int.MaxValue);
    }

    private static List<CaptureTriageTaskV1> ReduceMappedTasks(
        IReadOnlyList<IReadOnlyList<CaptureTriageTaskV1>> mappedTasks)
    {
        // A transcript can yield more than the v1 contract's 20 cards. Do not let an early chunk
        // consume every output slot: take one task from evenly spaced source chunks first, then
        // fill remaining slots in stable chunk/task order. This makes the lossy v1 reduction
        // deterministic while preserving coverage of both the beginning and end of a long meeting.
        var sanitizedByChunk = mappedTasks
            .Select(SanitizeTasks)
            .ToList();
        var reduced = new List<CaptureTriageTaskV1>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfUnique(CaptureTriageTaskV1 task)
        {
            if (reduced.Count < CaptureTriageOutputContract.MaxTasks && seenTitles.Add(task.Title))
            {
                reduced.Add(task);
            }
        }

        var coverageCount = Math.Min(sanitizedByChunk.Count, CaptureTriageOutputContract.MaxTasks);
        if (coverageCount > 0)
        {
            for (var slot = 0; slot < coverageCount; slot++)
            {
                var chunkIndex = coverageCount == 1
                    ? 0
                    : (int)((long)slot * (sanitizedByChunk.Count - 1) / (coverageCount - 1));
                if (sanitizedByChunk[chunkIndex].Count > 0)
                {
                    AddIfUnique(sanitizedByChunk[chunkIndex][0]);
                }
            }
        }

        for (var taskIndex = 0;
             reduced.Count < CaptureTriageOutputContract.MaxTasks &&
             sanitizedByChunk.Any(tasks => taskIndex < tasks.Count);
             taskIndex++)
        {
            foreach (var tasks in sanitizedByChunk)
            {
                if (taskIndex < tasks.Count)
                {
                    AddIfUnique(tasks[taskIndex]);
                }

                if (reduced.Count >= CaptureTriageOutputContract.MaxTasks)
                {
                    break;
                }
            }
        }

        return reduced;
    }

    /// <summary>
    /// Enforces the v1 contract caps on model output instead of rejecting near-valid responses:
    /// whitespace-normalized titles truncated to the title cap, evidence trimmed to the evidence cap
    /// (a truncated verbatim quote is still a verbatim prefix, so span recovery stays possible),
    /// duplicate titles dropped (mirroring the deterministic extractor's dedupe), capped at MaxTasks.
    /// </summary>
    private static List<CaptureTriageTaskV1> SanitizeTasks(IReadOnlyList<CaptureTriageTaskV1> rawTasks)
    {
        var sanitized = new List<CaptureTriageTaskV1>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in rawTasks)
        {
            var title = Regex.Replace(task.Title.Trim(), @"\s+", " ");
            if (title.Length > CaptureTriageOutputContract.MaxTaskTitleLength)
            {
                title = title[..CaptureTriageOutputContract.MaxTaskTitleLength].TrimEnd();
            }

            var evidence = task.Evidence.Trim();
            if (evidence.Length > CaptureTriageOutputContract.MaxTaskEvidenceLength)
            {
                evidence = evidence[..CaptureTriageOutputContract.MaxTaskEvidenceLength].TrimEnd();
            }

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(evidence) || !seenTitles.Add(title))
            {
                continue;
            }

            sanitized.Add(new CaptureTriageTaskV1(title, evidence));
            if (sanitized.Count >= CaptureTriageOutputContract.MaxTasks)
            {
                break;
            }
        }

        return sanitized;
    }
}
