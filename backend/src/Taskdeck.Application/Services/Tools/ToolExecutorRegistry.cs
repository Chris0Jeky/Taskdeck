namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Registry of tool executors, populated at startup and queried by the orchestrator.
/// Provides lookup by tool name for dispatching tool calls to the correct executor.
/// </summary>
public sealed class ToolExecutorRegistry
{
    private readonly Dictionary<string, IToolExecutor> _executors;

    public ToolExecutorRegistry(IEnumerable<IToolExecutor> executors)
    {
        _executors = executors.ToDictionary(
            e => e.ToolName,
            e => e,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns the executor for the given tool name, or null if not found.</summary>
    public IToolExecutor? GetExecutor(string toolName)
    {
        _executors.TryGetValue(toolName, out var executor);
        return executor;
    }

    /// <summary>Returns all registered tool names.</summary>
    public IReadOnlyCollection<string> GetRegisteredToolNames()
    {
        return _executors.Keys;
    }
}
