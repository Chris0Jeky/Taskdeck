using Taskdeck.Domain.Agents;

namespace Taskdeck.Application.Services;

/// <summary>
/// Evaluates whether a given agent profile is allowed to use a specific tool,
/// and under what constraints (review-first, direct apply, or deny).
/// </summary>
public interface IAgentPolicyEvaluator
{
    /// <summary>
    /// Evaluate whether the agent profile identified by <paramref name="agentProfileId"/>
    /// may invoke the tool identified by <paramref name="toolKey"/> in the given context.
    /// </summary>
    /// <param name="agentProfileId">The agent profile requesting tool use.</param>
    /// <param name="toolKey">The tool registry key to evaluate.</param>
    /// <param name="context">Optional contextual metadata (e.g. board ID, item count).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PolicyDecision"/> describing whether the action is allowed.</returns>
    Task<PolicyDecision> EvaluateToolUseAsync(
        Guid agentProfileId,
        string toolKey,
        IDictionary<string, string>? context = null,
        CancellationToken cancellationToken = default);
}
