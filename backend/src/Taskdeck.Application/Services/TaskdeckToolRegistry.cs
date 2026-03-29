using System.Collections.Concurrent;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// In-memory tool registry populated at application startup.
/// Thread-safe for concurrent reads; registration is expected during startup only.
/// </summary>
public sealed class TaskdeckToolRegistry : ITaskdeckToolRegistry
{
    private readonly ConcurrentDictionary<string, ITaskdeckTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterTool(ITaskdeckTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (string.IsNullOrWhiteSpace(tool.Key))
            throw new ArgumentException("Tool key cannot be null or empty.", nameof(tool));

        if (!_tools.TryAdd(tool.Key, tool))
            throw new InvalidOperationException($"A tool with key '{tool.Key}' is already registered.");
    }

    public ITaskdeckTool? GetTool(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        _tools.TryGetValue(key, out var tool);
        return tool;
    }

    public IReadOnlyList<ITaskdeckTool> GetAllTools()
    {
        return _tools.Values.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<ITaskdeckTool> GetToolsByScope(ToolScope scope)
    {
        return _tools.Values
            .Where(t => t.Scope == scope)
            .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
