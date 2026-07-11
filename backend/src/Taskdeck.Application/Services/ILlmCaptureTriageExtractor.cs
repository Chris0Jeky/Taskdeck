using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

/// <summary>
/// LLM-backed task extraction for transcript-source captures (REVIVAL-08 M1). Implementations
/// never throw for provider/config/parse problems — every non-success is reported as a
/// <see cref="LlmCaptureTriageOutcome"/> so the caller (<see cref="CaptureTriageService"/>) can
/// degrade to the deterministic extractor with honest provenance instead of failing the capture.
/// </summary>
public interface ILlmCaptureTriageExtractor
{
    Task<LlmCaptureTriageExtraction> ExtractAsync(
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken = default);
}

/// <summary>Why the LLM extraction leg did not produce a usable output.</summary>
public enum LlmCaptureTriageOutcome
{
    /// <summary>The LLM ran and produced a schema-valid, non-empty task list.</summary>
    Succeeded = 0,

    /// <summary>LLM transcript triage is disabled by configuration.</summary>
    Disabled,

    /// <summary>The capture-triage kill switch is active for this surface/user.</summary>
    KillSwitchActive,

    /// <summary>The resolved provider is the mock — no live provider is configured.</summary>
    ProviderIsMock,

    /// <summary>The resolved provider reports itself unavailable (invalid config).</summary>
    ProviderUnavailable,

    /// <summary>The per-user LLM quota denied the call.</summary>
    QuotaExceeded,

    /// <summary>The provider returned a degraded result (transport error, open circuit, truncation).</summary>
    ProviderDegraded,

    /// <summary>The completion did not parse into a schema-valid task list.</summary>
    InvalidOutput,

    /// <summary>
    /// The LLM ran successfully and deliberately reported zero action items. This is an extraction
    /// verdict, not a failure — callers must NOT degrade to the deterministic extractor (which would
    /// fabricate a card from unactionable text, the exact behavior REVIVAL-08 removes).
    /// </summary>
    EmptyExtraction,
}

/// <summary>
/// Result of the LLM extraction leg. <see cref="Output"/>, <see cref="Provider"/> and
/// <see cref="Model"/> are populated only when <see cref="Outcome"/> is
/// <see cref="LlmCaptureTriageOutcome.Succeeded"/>; recording them for any other outcome would be
/// false provenance (#1273).
/// </summary>
public sealed record LlmCaptureTriageExtraction(
    LlmCaptureTriageOutcome Outcome,
    CaptureTriageOutputV1? Output = null,
    string? Provider = null,
    string? Model = null,
    string? Detail = null)
{
    public bool Succeeded => Outcome == LlmCaptureTriageOutcome.Succeeded;
}
