using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// LLM-backed transcript task extraction (REVIVAL-08 M3). Runs the guardrail chain the chat surface
/// established (kill switch → provider health → quota → completion → usage recording), then parses
/// and sanitizes the completion into a contract-valid <see cref="CaptureTriageOutputV2"/>.
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
    private readonly LlmProviderDecision? _providerDecision;

    public LlmCaptureTriageExtractor(
        ILlmProvider llmProvider,
        LlmCaptureTriageSettings settings,
        ILlmKillSwitchService? killSwitchService = null,
        ILlmQuotaService? quotaService = null,
        ILogger<LlmCaptureTriageExtractor>? logger = null,
        ILlmCaptureTriageProgressReporter? progressReporter = null,
        LlmProviderDecision? providerDecision = null)
    {
        _llmProvider = llmProvider;
        _settings = settings;
        _killSwitchService = killSwitchService;
        _quotaService = quotaService;
        _logger = logger;
        _progressReporter = progressReporter;
        _providerDecision = providerDecision;
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
            var extraction = await ExtractChunkAsync(userId, boardId, payload, cancellationToken: cancellationToken);
            if (!extraction.Succeeded || extraction.Output is null)
            {
                return extraction;
            }

            var singleReduced = ReduceMappedTasks([
                new MappedChunkTasks(extraction.Output.Tasks
                    .Select((task, index) => new MappedTask(
                        task,
                        extraction.EvidenceSpans is not null && index < extraction.EvidenceSpans.Count
                            ? extraction.EvidenceSpans[index]
                            : null))
                    .ToList())
            ]);
            return extraction with
            {
                Output = extraction.Output with { Tasks = singleReduced.Tasks },
                EvidenceSpans = singleReduced.Spans
            };
        }

        if (chunks.Count > _settings.MaxChunkCount)
        {
            // Ask the guardrail chain FIRST. The budget decline below reports InvalidOutput, which
            // CaptureTriageService records as "LLM triage unavailable" - true only if a live leg
            // was ever going to run. On a deliberately offline deployment (triage disabled, or the
            // mock by choice) no model was expected, so a merely long transcript must not
            // manufacture a degradation notice. A kill switch, an unhealthy provider, or a mock
            // substituted for a REQUESTED live provider still report their own outcomes here, and
            // those are genuine degradations.
            var offlineDecline = await DeclineBeforeProviderWorkAsync(userId, cancellationToken);
            if (offlineDecline is not null)
            {
                return offlineDecline;
            }

            _logger?.LogWarning(
                "Transcript triage requires {ChunkCount} map chunks, exceeding configured maximum {MaxChunkCount}; using deterministic fallback without provider calls.",
                chunks.Count,
                _settings.MaxChunkCount);
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.InvalidOutput,
                Detail: "Transcript exceeds the configured map-chunk call budget.");
        }

        var mappedTasks = new List<MappedChunkTasks>(chunks.Count);
        string? provider = null;
        string? model = null;

        foreach (var chunk in chunks)
        {
            var chunkPayload = payload with { Text = chunk.Text };
            var extraction = await ExtractChunkAsync(userId, boardId, chunkPayload, chunk.Offset, cancellationToken);

            if (extraction.Succeeded)
            {
                if (!TrySetProviderIdentity(extraction, ref provider, ref model))
                {
                    return new LlmCaptureTriageExtraction(
                        LlmCaptureTriageOutcome.InvalidOutput,
                        Detail: "Chunked transcript extraction returned inconsistent provider provenance.");
                }

                mappedTasks.Add(new MappedChunkTasks(
                    extraction.Output!.Tasks
                        .Select((task, index) => new MappedTask(
                            task,
                            extraction.EvidenceSpans is not null && index < extraction.EvidenceSpans.Count
                                ? extraction.EvidenceSpans[index]
                                : null))
                        .ToList()));
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
                mappedTasks.Sum(tasks => tasks.Tasks.Count),
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

        var reduced = ReduceMappedTasks(mappedTasks);
        var output = new CaptureTriageOutputV2(
            CaptureTriageOutputContract.SchemaVersionV2,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            reduced.Tasks);
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
            model,
            EvidenceSpans: reduced.Spans);
    }

    /// <summary>
    /// The guardrail chain that decides whether a live LLM leg is possible at all, before any
    /// provider work or quota spend. Returns null when the leg may proceed.
    /// <para>
    /// Shared by the per-chunk path and the map-chunk budget decline so the two cannot drift: the
    /// budget decline reports InvalidOutput, which the caller records as a degradation, and that is
    /// only honest when a live leg was actually possible (#2192 review round 2).
    /// </para>
    /// </summary>
    private async Task<LlmCaptureTriageExtraction?> DeclineBeforeProviderWorkAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
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
            // The mock is reached two very different ways, and conflating them is what #2192 is
            // about. Mock BY CHOICE is an ordinary offline configuration and stays quiet. Mock by
            // DIVERSION - the operator asked for a live provider and selection rejected it, so
            // LlmProviderRegistration injected the mock instead - is a live provider that is not
            // working, and it was recorded nowhere but a startup log line.
            if (_providerDecision?.MockDiversionReason is { } diversionReason)
            {
                _logger?.LogWarning(
                    "Transcript triage for user {UserId} resolved to the mock provider after a live " +
                    "provider was requested: {DiversionReason}",
                    userId,
                    diversionReason);
                return new LlmCaptureTriageExtraction(
                    LlmCaptureTriageOutcome.ProviderUnavailable,
                    Detail: diversionReason);
            }

            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.ProviderIsMock);
        }

        if (!health.IsAvailable)
        {
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.ProviderUnavailable,
                Detail: health.ErrorMessage);
        }

        return null;
    }

    private async Task<LlmCaptureTriageExtraction> ExtractChunkAsync(
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        int sourceOffset = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var decline = await DeclineBeforeProviderWorkAsync(userId, cancellationToken);
        if (decline is not null)
        {
            return decline;
        }

        // Atomic quota reservation (issue #1313): reserve before the completion, then commit with the
        // actual tokens or release. This serializes concurrent triage/chat calls at the boundary.
        Guid? quotaReservationId = null;
        var quotaEstimatedTokens = 0;
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
            quotaEstimatedTokens = reservation.EstimatedTokens;
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

            // Settle before quality checks because degraded output can still be billed. Use
            // authoritative combined usage when present; otherwise commit the reservation estimate
            // for a dispatched call so short output cannot undercharge a large transcript.
            if (quotaReservationId is Guid reservationId)
            {
                var quotaTokens = ResolveQuotaTokens(result, quotaEstimatedTokens, request.DispatchContext);
                if (quotaTokens > 0)
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
                        quotaTokens,
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
                    var dispatch = request.DispatchContext.ReadSnapshot();
                    var quotaTokens = completed is null
                        ? dispatch.Phase == LlmDispatchPhase.Dispatched
                            ? quotaEstimatedTokens
                            : 0
                        : ResolveQuotaTokens(completed, quotaEstimatedTokens, request.DispatchContext);
                    var billedProvider = completed?.Provider ?? dispatch.Provider;
                    var billedModel = completed?.Model ?? dispatch.Model;
                    if (quotaTokens > 0 && billedProvider is not null && billedModel is not null)
                    {
                        await _quotaService!.CommitReservationAsync(
                            unsettledId,
                            userId,
                            LlmSurface.CaptureTriage,
                            billedProvider,
                            billedModel,
                            quotaTokens,
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
                        completed is null
                            ? request.DispatchContext.ReadSnapshot().Phase == LlmDispatchPhase.Dispatched
                                ? quotaEstimatedTokens
                                : 0
                            : ResolveQuotaTokens(completed, quotaEstimatedTokens, request.DispatchContext));
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

        var sanitized = SanitizeTasks(rawTasks, payload.Text, sourceOffset);
        if (sanitized.Tasks.Count == 0)
        {
            // The model returned entries but none survived sanitization — treat as malformed
            // output (fallback), not as a deliberate empty verdict.
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.InvalidOutput);
        }

        var output = new CaptureTriageOutputV2(
            CaptureTriageOutputContract.SchemaVersionV2,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            sanitized.Tasks);

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
            result.Model,
            EvidenceSpans: sanitized.Spans);
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

    private sealed record MappedTask(CaptureTriageTaskV2 Task, (int Start, int End)? Span);

    private sealed record MappedChunkTasks(IReadOnlyList<MappedTask> Tasks);

    private sealed record ReducedTasks(
        IReadOnlyList<CaptureTriageTaskV2> Tasks,
        IReadOnlyList<(int Start, int End)?> Spans);

    private static ReducedTasks ReduceMappedTasks(
        IReadOnlyList<MappedChunkTasks> mappedTasks)
    {
        // A transcript can yield more than the schema-v2 contract's 20 cards. Do not let an early chunk
        // consume every output slot: take one task from evenly spaced source chunks first, then
        // fill remaining slots in stable chunk/task order. This makes the lossy reduction
        // deterministic while preserving coverage of both the beginning and end of a long meeting.
        var sanitizedByChunk = mappedTasks
            .Select(chunk => chunk.Tasks.ToList())
            .ToList();
        var reduced = new List<CaptureTriageTaskV2>();
        var reducedSpans = new List<(int Start, int End)?>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfUnique(CaptureTriageTaskV2 task)
        {
            if (reduced.Count < CaptureTriageOutputContract.MaxTasks && seenTitles.Add(task.Title))
            {
                reduced.Add(task);
                reducedSpans.Add(ResolveConsensusSpan(
                    sanitizedByChunk
                        .SelectMany(tasks => tasks)
                        .Where(candidate => string.Equals(candidate.Task.Title, task.Title, StringComparison.OrdinalIgnoreCase))
                        .Select(candidate => candidate.Span)));
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
                    AddIfUnique(sanitizedByChunk[chunkIndex][0].Task);
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
                    AddIfUnique(tasks[taskIndex].Task);
                }

                if (reduced.Count >= CaptureTriageOutputContract.MaxTasks)
                {
                    break;
                }
            }
        }

        return new ReducedTasks(reduced, reducedSpans);
    }

    private static (int Start, int End)? ResolveConsensusSpan(
        IEnumerable<(int Start, int End)?> observations)
    {
        var spans = observations.ToList();
        if (spans.Count == 0 || spans.Any(span => !span.HasValue))
        {
            return null;
        }

        var first = spans[0]!.Value;
        return spans.All(span => span!.Value == first) ? first : null;
    }

    /// <summary>
    /// Enforces schema-v2 limits on model output. The extractor only preserves model items whose
    /// quote is an exact substring of the source chunk: without that boundary, later span linking
    /// could turn a model hallucination into misleading provenance. Assignee/due-date hints stay
    /// descriptive values here; no identity resolution or date mutation happens in this layer.
    /// </summary>
    private static (IReadOnlyList<CaptureTriageTaskV2> Tasks, IReadOnlyList<(int Start, int End)?> Spans) SanitizeTasks(
        IReadOnlyList<CaptureTriageTaskV2> rawTasks,
        string transcriptText,
        int sourceOffset)
    {
        // Do not repair model output here. A completion that misses the v2 contract cannot safely
        // retain a subset of its metadata, and truncating or normalizing a claimed verbatim quote
        // would make later evidence linkage misleading. A malformed map leg therefore makes the
        // whole extraction fall back rather than silently omitting or changing evidence.
        var validation = CaptureTriageOutputContract.Validate(new CaptureTriageOutputV2(
            CaptureTriageOutputContract.SchemaVersionV2,
            CaptureTriageOutputContract.PromptVersionLlmV2,
            rawTasks));
        if (!validation.IsSuccess)
        {
            return (Array.Empty<CaptureTriageTaskV2>(), Array.Empty<(int Start, int End)?>());
        }

        var sanitized = new List<CaptureTriageTaskV2>();
        var spans = new List<(int Start, int End)?>();

        foreach (var task in validation.Value.Tasks)
        {
            if (!transcriptText.Contains(task.EvidenceQuote, StringComparison.Ordinal))
            {
                return (Array.Empty<CaptureTriageTaskV2>(), Array.Empty<(int Start, int End)?>());
            }

            sanitized.Add(task);
            spans.Add(FindUniqueAbsoluteSpan(transcriptText, task.EvidenceQuote, sourceOffset));
        }

        return (sanitized, spans);
    }

    private static (int Start, int End)? FindUniqueAbsoluteSpan(
        string text,
        string quote,
        int sourceOffset)
    {
        var first = text.IndexOf(quote, StringComparison.Ordinal);
        if (first < 0 || text.IndexOf(quote, first + 1, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        return (sourceOffset + first, sourceOffset + first + quote.Length);
    }

    private static int ResolveQuotaTokens(
        LlmCompletionResult result,
        int reservationEstimate,
        LlmDispatchContext dispatchContext)
    {
        var dispatch = dispatchContext.ReadSnapshot();
        if (dispatch.Phase == LlmDispatchPhase.ObservedPreDispatch)
            return 0;
        if (dispatch.Phase == LlmDispatchPhase.Dispatched)
            return result.HasAuthoritativeTokenUsage && result.TokensUsed > 0
                ? result.TokensUsed
                : reservationEstimate;

        return result.HasAuthoritativeTokenUsage
            ? result.TokensUsed
            : result.ShouldSettleQuotaReservation
                ? reservationEstimate
                : 0;
    }
}
