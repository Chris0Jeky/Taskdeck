using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for LLM tool-calling behaviour.
/// Bound from the "LlmToolCalling" configuration section.
/// </summary>
public class LlmToolCallingSettings
{
    /// <summary>
    /// Enables or disables the multi-turn tool-calling orchestrator.
    /// When false, <see cref="ChatService"/> falls through to the single-turn
    /// <see cref="ILlmProvider.CompleteAsync"/> path for every request.
    /// Default is true so existing behaviour is preserved.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum byte length of a single tool result before it is truncated.
    /// Keeps oversized responses within the provider's context window.
    /// 0 = no truncation limit (not recommended for production).
    /// Default is 8 000 bytes (roughly a few thousand tokens depending on content and tokenizer).
    /// </summary>
    [Range(0, 1_000_000, ErrorMessage = "MaxToolResultBytes must be between 0 and 1000000.")]
    public int MaxToolResultBytes { get; set; } = 8_000;

    /// <summary>
    /// Maximum number of session messages included in a completion request.
    /// Long sessions send only the most recent messages (sliding window), keeping
    /// prompts within the provider context window and bounding per-turn cost.
    /// The system prompt travels separately and is unaffected. Default is 50.
    /// </summary>
    [Range(1, 1_000, ErrorMessage = "MaxHistoryMessages must be between 1 and 1000.")]
    public int MaxHistoryMessages { get; set; } = 50;
}
