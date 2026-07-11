using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// LLM-backed transcript task extraction (REVIVAL-08 M1). Runs the guardrail chain the chat surface
/// established (kill switch → provider health → quota → completion → usage recording), then parses
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

        if (_quotaService is not null)
        {
            var quota = await _quotaService.CheckQuotaAsync(userId, LlmSurface.CaptureTriage, cancellationToken);
            if (!quota.Allowed)
            {
                return new LlmCaptureTriageExtraction(
                    LlmCaptureTriageOutcome.QuotaExceeded,
                    Detail: quota.DeniedReason);
            }
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

        var result = await _llmProvider.CompleteAsync(request, cancellationToken);

        // Tokens are consumed whether or not the content is usable (truncation is the clearest
        // case: degraded AND billed), so usage is recorded before any quality checks.
        if (_quotaService is not null && result.TokensUsed > 0)
        {
            await _quotaService.RecordUsageAsync(
                userId,
                LlmSurface.CaptureTriage,
                result.Provider,
                result.Model,
                result.TokensUsed,
                0,
                cancellationToken);
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
            return new LlmCaptureTriageExtraction(LlmCaptureTriageOutcome.EmptyExtraction);
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
