using Taskdeck.Domain.Agents;

namespace Taskdeck.Application.Services;

/// <summary>
/// Validates agent tool bundles against permanent exclusion rules
/// and the tool registry. Implements GP-06 (review-first automation safety):
/// agents cannot approve proposals, directly mutate boards, or invoke unknown tools.
/// Fail-closed: any tool not in the registry is denied.
/// </summary>
public sealed class AgentPolicy
{
    /// <summary>
    /// Tools that are permanently excluded from all agent bundles.
    /// These tools perform direct mutations or bypass the review gate.
    /// </summary>
    public static readonly HashSet<string> PermanentlyExcludedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "approve_proposal",
        "apply_proposal",
        "board.direct_update",
        "board.direct_delete",
        "card.direct_move",
        "card.direct_delete",
        "column.direct_delete"
    };

    private readonly ITaskdeckToolRegistry _toolRegistry;

    public AgentPolicy(ITaskdeckToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
    }

    /// <summary>
    /// Validates a set of requested tools against permanent exclusion rules
    /// and the tool registry. Returns a decision for each tool.
    /// </summary>
    public IReadOnlyList<ToolBundleDecision> ValidateToolBundle(IEnumerable<string> requestedTools)
    {
        ArgumentNullException.ThrowIfNull(requestedTools);

        var decisions = new List<ToolBundleDecision>();

        foreach (var toolKey in requestedTools)
        {
            if (string.IsNullOrWhiteSpace(toolKey))
            {
                decisions.Add(new ToolBundleDecision(toolKey ?? "", false, "Tool key cannot be empty."));
                continue;
            }

            if (PermanentlyExcludedTools.Contains(toolKey))
            {
                decisions.Add(new ToolBundleDecision(toolKey, false,
                    $"Tool '{toolKey}' is permanently excluded from agent bundles (GP-06: review-first safety)."));
                continue;
            }

            var tool = _toolRegistry.GetTool(toolKey);
            if (tool is null)
            {
                decisions.Add(new ToolBundleDecision(toolKey, false,
                    $"Tool '{toolKey}' is not registered. Fail-closed: unknown tools are denied."));
                continue;
            }

            decisions.Add(new ToolBundleDecision(toolKey, true,
                $"Tool '{tool.DisplayName}' ({tool.RiskLevel} risk) is allowed."));
        }

        return decisions;
    }
}

/// <summary>
/// The result of validating a single tool in an agent tool bundle.
/// </summary>
public sealed record ToolBundleDecision(
    string ToolKey,
    bool Allowed,
    string Reason);
