using Microsoft.Extensions.Logging;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// Validates agent tool bundles against security policies.
/// Implements fail-closed defaults: unknown tools are denied.
/// GP-06: approve_proposal and direct board mutation tools are permanently excluded.
/// GP-09: all policy decisions are inspectable.
/// </summary>
public sealed class AgentPolicy
{
    /// <summary>
    /// Tools that agents are NEVER allowed to use, regardless of policy configuration.
    /// approve_proposal and any direct board mutation tools are permanently excluded
    /// to enforce review-first automation safety (GP-06).
    /// </summary>
    private static readonly HashSet<string> PermanentlyExcludedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "approve_proposal",
        "apply_proposal",
        "board.direct_update",
        "board.direct_delete",
        "card.direct_move",
        "card.direct_delete",
        "column.direct_delete",
    };

    private readonly ITaskdeckToolRegistry _toolRegistry;
    private readonly ILogger<AgentPolicy>? _logger;

    public AgentPolicy(ITaskdeckToolRegistry toolRegistry, ILogger<AgentPolicy>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger;
    }

    /// <summary>
    /// Validates that a set of tool keys is allowed for an agent to use.
    /// Returns a list of policy decisions, one per tool.
    /// Fail-closed: any tool not in the registry or in the exclusion list is denied.
    /// </summary>
    public IReadOnlyList<ToolBundleDecision> ValidateToolBundle(
        IEnumerable<string> requestedToolKeys,
        IReadOnlyList<string>? profileAllowlist = null)
    {
        ArgumentNullException.ThrowIfNull(requestedToolKeys);

        var decisions = new List<ToolBundleDecision>();

        foreach (var toolKey in requestedToolKeys)
        {
            if (string.IsNullOrWhiteSpace(toolKey))
            {
                decisions.Add(new ToolBundleDecision(toolKey ?? string.Empty, false,
                    "Tool key is empty or null."));
                continue;
            }

            // Check permanent exclusion list first
            if (PermanentlyExcludedTools.Contains(toolKey))
            {
                _logger?.LogWarning(
                    "Tool '{ToolKey}' is permanently excluded from agent use (GP-06)", toolKey);
                decisions.Add(new ToolBundleDecision(toolKey, false,
                    $"Tool '{toolKey}' is permanently excluded. Agents cannot approve proposals or directly mutate boards."));
                continue;
            }

            // Check tool exists in registry
            var tool = _toolRegistry.GetTool(toolKey);
            if (tool is null)
            {
                _logger?.LogWarning("Tool '{ToolKey}' not found in registry — denied by default", toolKey);
                decisions.Add(new ToolBundleDecision(toolKey, false,
                    $"Tool '{toolKey}' is not registered. Unknown tools are denied."));
                continue;
            }

            // Check profile-level allowlist if specified (fail-closed: empty allowlist denies all)
            if (profileAllowlist is not null &&
                !profileAllowlist.Any(a => string.Equals(a, toolKey, StringComparison.OrdinalIgnoreCase)))
            {
                decisions.Add(new ToolBundleDecision(toolKey, false,
                    $"Tool '{toolKey}' is not in the agent profile's allowed tool list."));
                continue;
            }

            decisions.Add(new ToolBundleDecision(toolKey, true,
                $"Tool '{tool.DisplayName}' is allowed."));
        }

        return decisions;
    }

    /// <summary>
    /// Returns true if the tool key is in the permanently excluded set.
    /// </summary>
    public static bool IsPermanentlyExcluded(string toolKey)
    {
        return PermanentlyExcludedTools.Contains(toolKey);
    }

    /// <summary>
    /// Returns the set of permanently excluded tool keys (for inspection/auditing).
    /// </summary>
    public static IReadOnlySet<string> GetPermanentlyExcludedTools()
    {
        return PermanentlyExcludedTools;
    }
}

/// <summary>
/// Result of a single tool's policy validation within a bundle.
/// </summary>
public sealed record ToolBundleDecision(
    string ToolKey,
    bool Allowed,
    string Reason);
