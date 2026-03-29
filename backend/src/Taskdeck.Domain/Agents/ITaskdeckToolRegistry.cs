using Taskdeck.Domain.Enums;

namespace Taskdeck.Domain.Agents;

/// <summary>
/// Registry of all available agent tools. Populated at application startup
/// and queried by the policy evaluator and agent templates.
/// </summary>
public interface ITaskdeckToolRegistry
{
    /// <summary>Register a tool. Throws if a tool with the same key is already registered.</summary>
    void RegisterTool(ITaskdeckTool tool);

    /// <summary>Look up a tool by its unique key. Returns null if not found.</summary>
    ITaskdeckTool? GetTool(string key);

    /// <summary>Return all registered tools.</summary>
    IReadOnlyList<ITaskdeckTool> GetAllTools();

    /// <summary>Return tools filtered by operational scope.</summary>
    IReadOnlyList<ITaskdeckTool> GetToolsByScope(ToolScope scope);
}
