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
    /// Default is 8 000 bytes (~6 000 tokens at typical tokenisation ratios).
    /// </summary>
    public int MaxToolResultBytes { get; set; } = 8_000;
}
