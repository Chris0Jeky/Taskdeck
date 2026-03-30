namespace Taskdeck.Application.Services;

/// <summary>
/// Shared helpers for constructing the effective system prompt sent to LLM providers.
/// Centralises the board context append logic so neither provider duplicates it.
/// </summary>
internal static class LlmSystemPromptBuilder
{
    /// <summary>
    /// Returns the effective system prompt by appending the board context (when present)
    /// to the base system prompt.
    /// </summary>
    /// <param name="systemPrompt">The base system prompt, which may be null or empty.</param>
    /// <param name="boardContext">Optional board context string from <see cref="IBoardContextBuilder"/>.</param>
    /// <returns>The combined prompt, or the base prompt unchanged when no board context is available.</returns>
    public static string BuildEffectiveSystemPrompt(string? systemPrompt, string? boardContext)
    {
        if (string.IsNullOrEmpty(boardContext))
            return systemPrompt ?? string.Empty;

        return string.IsNullOrEmpty(systemPrompt)
            ? boardContext
            : $"{systemPrompt}\n\n{boardContext}";
    }
}
