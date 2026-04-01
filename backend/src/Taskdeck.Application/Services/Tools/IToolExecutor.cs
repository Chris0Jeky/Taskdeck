using System.Text.Json;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes a single tool invocation against the board data layer.
/// Each implementation handles one specific tool (e.g., list_cards_in_column).
/// </summary>
public interface IToolExecutor
{
    /// <summary>The tool name this executor handles (must match TaskdeckToolSchema.Name).</summary>
    string ToolName { get; }

    /// <summary>
    /// Executes the tool with the given arguments, scoped to the specified board.
    /// Returns a JSON string result.
    /// </summary>
    Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default);
}
