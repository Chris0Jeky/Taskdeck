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

    /// <summary>
    /// Conservative input-token budget for one transcript chunk. Long transcript captures are split
    /// before the existing guarded extraction call, leaving the output budget independent and
    /// avoiding a provider-specific tokenizer dependency.
    /// </summary>
    [Range(1024, 32768)]
    public int MaxInputTokensPerChunk { get; set; } = 12000;

    /// <summary>
    /// Context retained at the start of a following chunk. The chunk planner caps this to one quarter
    /// of <see cref="MaxInputTokensPerChunk"/> so a malformed configuration cannot prevent progress.
    /// </summary>
    [Range(0, 4096)]
    public int ChunkOverlapTokens { get; set; } = 256;

    /// <summary>
    /// Maximum provider calls permitted for one map-reduce triage run. If the configured token
    /// budget would require more chunks, the extraction leg declines before reserving quota and
    /// <see cref="CaptureTriageService"/> uses its existing deterministic fallback for the whole
    /// capture instead of issuing an unbounded sequence of partial calls.
    /// </summary>
    [Range(1, 128)]
    public int MaxChunkCount { get; set; } = 24;

    /// <summary>Sampling temperature for extraction. Low by default: fidelity over creativity.</summary>
    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.1;
}
