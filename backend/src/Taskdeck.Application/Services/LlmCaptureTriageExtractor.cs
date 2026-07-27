using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// LLM-backed transcript task extraction (REVIVAL-08 M1). Runs the guardrail chain the chat surface
/// established (kill switch → provider health → quota → completion → usage recording), then parses
/// and strictly contains the completion into a contract-valid <see cref="CaptureTriageOutputV1"/>.
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

    public LlmCaptureTriageExtractor(
        ILlmProvider llmProvider,
        LlmCaptureTriageSettings settings,
        ILlmKillSwitchService? killSwitchService = null,
        ILlmQuotaService? quotaService = null,
        ILogger<LlmCaptureTriageExtractor>? logger = null)
    {
        _llmProvider = llmProvider;
        _settings = settings;
        _killSwitchService = killSwitchService;
        _quotaService = quotaService;
        _logger = logger;
    }

    public async Task<LlmCaptureTriageExtraction> ExtractAsync(
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
            var reservation = await _quotaService.ReserveAsync(userId, LlmSurface.CaptureTriage, cancellationToken);
            if (!reservation.Allowed)
            {
                return new LlmCaptureTriageExtraction(
                    LlmCaptureTriageOutcome.QuotaExceeded,
                    Detail: reservation.DeniedReason);
            }
            quotaReservationId = reservation.ReservationId;
        }

        var request = new ChatCompletionRequest(
            Messages: [new ChatCompletionMessage("user", LlmCaptureTriagePrompt.BuildUserMessage(payload.Text))],
            MaxTokens: _settings.MaxOutputTokens,
            Temperature: _settings.Temperature,
            Attribution: new LlmRequestAttribution(
                userId,
                LlmRequestAttributionMapper.ResolveCorrelationIdFromActivity(),
                LlmRequestSourceSurface.Capture,
                boardId),
            // A non-null SystemPrompt opts out of the providers' chat instruction-extraction mode.
            // The prompt frames capture text as untrusted data and TryParseTasks accepts only the
            // exact raw-JSON response vocabulary.
            SystemPrompt: LlmCaptureTriagePrompt.SystemPrompt);

        LlmCompletionResult result;
        LlmCompletionResult? completed = null;
        var quotaSettled = quotaReservationId is null;
        try
        {
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
                Model: result.Model,
                PromptVersion: LlmCaptureTriagePrompt.PromptVersion);
        }

        var ungroundedEvidenceCount = rawTasks.Count(task =>
            !payload.Text.Contains(task.Evidence, StringComparison.Ordinal));
        if (ungroundedEvidenceCount > 0)
        {
            // Every evidence value must be an exact ordinal substring of the original source.
            // Ungrounded output is malformed and takes the deterministic fallback path.
            _logger?.LogWarning(
                "LLM transcript triage returned {UngroundedEvidenceCount} ungrounded evidence value(s) for user {UserId}; using deterministic fallback",
                ungroundedEvidenceCount,
                userId);
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.InvalidOutput);
        }

        var output = new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            LlmCaptureTriagePrompt.PromptVersion,
            rawTasks);

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
            PromptVersion: LlmCaptureTriagePrompt.PromptVersion);
    }
}
