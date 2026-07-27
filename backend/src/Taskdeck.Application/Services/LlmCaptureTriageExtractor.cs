using System.Text.RegularExpressions;
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
    private static readonly TimeSpan SourceSignalTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly string[] ExplicitTaskVerbs =
    [
        "add", "assign", "book", "call", "check", "complete", "contact", "create", "deploy",
        "draft", "email", "finalize", "fix", "follow up", "investigate", "meet", "organize",
        "prepare", "publish", "remove", "review", "rotate", "schedule", "send", "ship", "submit",
        "test", "update", "verify", "write"
    ];

    private static readonly Regex SpeakerCommitmentPrefixPattern = new(
        @"^[^\r\n:]{1,80}:\s*(?:I|we)\s+(?:will|shall|must|can|need\s+to|plan\s+to|am\s+going\s+to|are\s+going\s+to)\s+(?<task>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase |
        RegexOptions.Multiline | RegexOptions.NonBacktracking,
        SourceSignalTimeout);

    private static readonly Regex ContractedSpeakerCommitmentPrefixPattern = new(
        @"^[^\r\n:]{1,80}:\s*(?:I|we)['\u2019]ll\s+(?<task>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase |
        RegexOptions.Multiline | RegexOptions.NonBacktracking,
        SourceSignalTimeout);

    private static readonly Regex NamedAssignmentPrefixPattern = new(
        @"^\s*[\p{Lu}][\p{L}\p{M}'’.-]{1,50}(?:\s+[\p{Lu}][\p{L}\p{M}'’.-]{1,50})?\s+(?:will|shall|must|needs\s+to|is\s+going\s+to)\s+(?<task>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline |
        RegexOptions.NonBacktracking,
        SourceSignalTimeout);

    private static readonly Regex StructuredTaskPrefixPattern = new(
        @"^\s*(?:(?<bullet>[-*•])\s+|\d+[.)]\s+)(?<task>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline |
        RegexOptions.NonBacktracking,
        SourceSignalTimeout);

    private static readonly Regex ExplicitTaskMarkerPrefixPattern = new(
        @"^\s*(?:(?:please|let['\u2019]s)\s+|(?:action\s+item|next\s+step|todo)\s*:\s*)" +
        @"(?<task>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase |
        RegexOptions.Multiline | RegexOptions.NonBacktracking,
        SourceSignalTimeout);

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
        var quotaEstimatedTokens = 0;
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
            quotaEstimatedTokens = reservation.EstimatedTokens;
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
            // Capture triage explicitly opts into raw response preservation. The prompt frames
            // capture text as untrusted data and TryParseTasks accepts only the exact raw-JSON
            // response vocabulary.
            SystemPrompt: LlmCaptureTriagePrompt.SystemPrompt)
        {
            ResponseMode = LlmCompletionResponseMode.CaptureTriageRaw
        };

        LlmCompletionResult result;
        LlmCompletionResult? completed = null;
        var quotaSettled = quotaReservationId is null;
        try
        {
            result = await _llmProvider.CompleteAsync(request, cancellationToken);
            completed = result;

            // Settle the reservation. Tokens are consumed whether or not the content is usable
            // (truncation is the clearest case: degraded AND billed), so a call that used tokens commits
            // before any quality checks. A response-deadline fallback has no trustworthy provider count,
            // so it commits the reservation estimate without exposing that estimate as observed usage.
            if (quotaReservationId is Guid reservationId)
            {
                var quotaTokens = ResolveQuotaTokens(result, quotaEstimatedTokens);
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
            // Mirrors ChatService (#1427 review): SETTLE an unfinalized reservation. If the provider
            // completed with billed tokens (the in-try commit itself failed on a DB fault), COMMIT them —
            // including a flagged unknown-usage timeout at the reservation estimate. A provider throw
            // or an unflagged zero-token result releases so the slot consumes no quota.
            // CancellationToken.None lets cleanup run under a cancelled request token; try/catch keeps
            // a settle failure from masking the original exception.
            if (!quotaSettled && quotaReservationId is Guid unsettledId)
            {
                try
                {
                    var quotaTokens = completed is null
                        ? 0
                        : ResolveQuotaTokens(completed, quotaEstimatedTokens);
                    if (completed is { } billed && quotaTokens > 0)
                    {
                        await _quotaService!.CommitReservationAsync(
                            unsettledId,
                            userId,
                            LlmSurface.CaptureTriage,
                            billed.Provider,
                            billed.Model,
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
                        completed is null ? 0 : ResolveQuotaTokens(completed, quotaEstimatedTokens));
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
            if (RequiresReviewForEmptyVerdict(payload.Text))
            {
                // Empty remains a terminal verdict for ordinary discussion, but it must not close a
                // capture that contains a conservative, source-local human-task signal. Mark the
                // output invalid so the existing deterministic fallback creates a review proposal.
                _logger?.LogWarning(
                    "LLM transcript triage returned an empty verdict that contradicted a source task signal for user {UserId}; using deterministic review fallback",
                    userId);
                return new LlmCaptureTriageExtraction(
                    LlmCaptureTriageOutcome.InvalidOutput,
                    Detail: "Empty verdict contradicted a conservative source task signal");
            }

            // The LLM ran and deliberately reported zero action items — a real verdict, so the
            // provider/model that produced it are reported for honest provenance stamping.
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.EmptyExtraction,
                Provider: result.Provider,
                Model: result.Model)
            {
                PromptVersion = LlmCaptureTriagePrompt.PromptVersion
            };
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
            result.Model)
        {
            PromptVersion = LlmCaptureTriagePrompt.PromptVersion
        };
    }

    internal static bool RequiresReviewForEmptyVerdict(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        try
        {
            return HasExplicitTaskVerbAfterPrefix(SpeakerCommitmentPrefixPattern, source) ||
                   HasExplicitTaskVerbAfterPrefix(ContractedSpeakerCommitmentPrefixPattern, source) ||
                   HasExplicitTaskVerbAfterPrefix(NamedAssignmentPrefixPattern, source) ||
                   HasStructuredTaskSignal(source) ||
                   HasExplicitTaskVerbAfterPrefix(ExplicitTaskMarkerPrefixPattern, source);
        }
        catch (RegexMatchTimeoutException)
        {
            // A timeout must never convert an uncertain source into a terminal no-task verdict.
            return true;
        }
    }

    private static bool HasExplicitTaskVerbAfterPrefix(Regex prefixPattern, string source)
    {
        foreach (Match match in prefixPattern.Matches(source))
        {
            var taskText = match.Groups["task"].Value.TrimStart();
            foreach (var verb in ExplicitTaskVerbs)
            {
                if (!taskText.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (taskText.Length == verb.Length || !IsWordCharacter(taskText[verb.Length]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasStructuredTaskSignal(string source)
    {
        foreach (Match match in StructuredTaskPrefixPattern.Matches(source))
        {
            var taskText = match.Groups["task"].Value.TrimStart();
            if (!match.Groups["bullet"].Success || !IsCompletedChecklistTask(taskText))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCompletedChecklistTask(string taskText)
    {
        return taskText.Length >= 3 &&
               taskText[0] == '[' &&
               taskText[1] is 'x' or 'X' &&
               taskText[2] == ']' &&
               (taskText.Length == 3 || char.IsWhiteSpace(taskText[3]));
    }

    private static int ResolveQuotaTokens(LlmCompletionResult result, int estimatedTokens)
    {
        if (result.TokensUsed > 0)
        {
            return result.TokensUsed;
        }

        return result.ShouldCommitEstimatedUsage ? Math.Max(0, estimatedTokens) : 0;
    }

    private static bool IsWordCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';
}
