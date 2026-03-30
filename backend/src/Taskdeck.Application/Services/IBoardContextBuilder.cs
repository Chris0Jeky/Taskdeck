namespace Taskdeck.Application.Services;

/// <summary>
/// Builds a bounded board context string for inclusion in LLM system prompts.
/// Returns null when the board does not exist.
/// </summary>
public interface IBoardContextBuilder
{
    Task<string?> BuildContextAsync(Guid boardId, CancellationToken ct = default);
}
