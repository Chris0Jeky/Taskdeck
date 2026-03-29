using Taskdeck.Domain.Enums;

namespace Taskdeck.Domain.Agents;

/// <summary>
/// Describes a tool that an agent can invoke. Tools are registered in the
/// tool registry and evaluated by the policy engine before execution.
/// </summary>
public interface ITaskdeckTool
{
    /// <summary>Unique machine-readable key (e.g. "inbox.triage", "board.create-card").</summary>
    string Key { get; }

    /// <summary>Human-readable display name shown in review UIs.</summary>
    string DisplayName { get; }

    /// <summary>Short description of what this tool does.</summary>
    string Description { get; }

    /// <summary>The operational scope this tool acts within.</summary>
    ToolScope Scope { get; }

    /// <summary>Risk classification used by the policy evaluator.</summary>
    ToolRiskLevel RiskLevel { get; }
}
