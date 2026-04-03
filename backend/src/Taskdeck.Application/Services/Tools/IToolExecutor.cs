using System.Text.Json;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Context passed to tool executors, providing board scope and user identity.
/// </summary>
public record ToolExecutionContext(Guid BoardId, Guid UserId);

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

    /// <summary>
    /// Executes the tool with the given arguments and execution context.
    /// Default implementation delegates to the boardId-only overload for backward compatibility.
    /// Write tools override this to access the userId for proposal creation.
    /// </summary>
    Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
        => ExecuteAsync(context.BoardId, arguments, ct);
}
