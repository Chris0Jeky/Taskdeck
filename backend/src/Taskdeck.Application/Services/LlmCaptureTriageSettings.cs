using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Settings for the LLM-backed transcript triage strategy (REVIVAL-08 M1), bound from the
/// <c>CaptureTriageLlm</c> configuration section. The strategy only ever runs for transcript-source
/// captures when a live (non-mock) provider resolves; disabling it — or any provider/kill-switch/
/// quota/parse failure — degrades to the deterministic extractor, never to a failed capture.
/// </summary>
public class LlmCaptureTriageSettings
{
    /// <summary>
    /// Master switch for LLM transcript triage. When false, transcript captures triage through the
    /// deterministic extractor exactly as before REVIVAL-08.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Completion-token budget for the extraction response. Sized for the worst-case v1 output
    /// (20 tasks × (180-char title + 280-char evidence) ≈ 9.2 KB JSON ≈ 2.5–3k tokens) with headroom;
    /// a truncated response is detected as degraded by the provider and falls back deterministically.
    /// </summary>
    [Range(256, 32768)]
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>Sampling temperature for extraction. Low by default: fidelity over creativity.</summary>
    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.1;
}
