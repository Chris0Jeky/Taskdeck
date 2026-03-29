using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Evaluates agent tool-use requests against the profile's policy configuration
/// and the tool's risk level. Review-first is the default for all risk levels;
/// low-risk auto-apply is OFF unless explicitly opted in via policy.
/// </summary>
public sealed class AgentPolicyEvaluator : IAgentPolicyEvaluator
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskdeckToolRegistry _toolRegistry;
    private readonly ILogger<AgentPolicyEvaluator>? _logger;

    public AgentPolicyEvaluator(
        IUnitOfWork unitOfWork,
        ITaskdeckToolRegistry toolRegistry,
        ILogger<AgentPolicyEvaluator>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public async Task<PolicyDecision> EvaluateToolUseAsync(
        Guid agentProfileId,
        string toolKey,
        IDictionary<string, string>? context = null,
        CancellationToken cancellationToken = default)
    {
        if (agentProfileId == Guid.Empty)
        {
            _logger?.LogWarning("Policy evaluation denied: empty agent profile ID");
            return PolicyDecision.Deny("Agent profile ID is required.");
        }

        if (string.IsNullOrWhiteSpace(toolKey))
        {
            _logger?.LogWarning("Policy evaluation denied: empty tool key");
            return PolicyDecision.Deny("Tool key is required.");
        }

        // Look up the tool in the registry
        var tool = _toolRegistry.GetTool(toolKey);
        if (tool is null)
        {
            _logger?.LogWarning("Policy evaluation denied: tool '{ToolKey}' not found in registry", toolKey);
            return PolicyDecision.Deny($"Tool '{toolKey}' is not registered.");
        }

        // Look up the agent profile
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(agentProfileId, cancellationToken);
        if (profile is null)
        {
            _logger?.LogWarning("Policy evaluation denied: agent profile '{ProfileId}' not found", agentProfileId);
            return PolicyDecision.Deny("Agent profile not found.");
        }

        if (!profile.IsEnabled)
        {
            _logger?.LogInformation(
                "Policy evaluation denied: agent profile '{ProfileId}' is disabled", agentProfileId);
            return PolicyDecision.Deny("Agent profile is disabled.");
        }

        // Parse the profile's policy JSON
        var policy = ParsePolicy(profile.PolicyJson);

        // Check tool allowlist: if a non-empty allowlist is defined, the tool must be in it
        if (policy.AllowedTools.Count > 0 && !policy.AllowedTools.Contains(toolKey, StringComparer.OrdinalIgnoreCase))
        {
            _logger?.LogInformation(
                "Policy evaluation denied: tool '{ToolKey}' not in allowlist for profile '{ProfileId}'",
                toolKey, agentProfileId);
            return PolicyDecision.Deny($"Tool '{toolKey}' is not in this agent's allowed tool list.");
        }

        // Enforce risk-level constraints
        // High risk: always requires review, never auto-apply
        if (tool.RiskLevel == ToolRiskLevel.High)
        {
            _logger?.LogInformation(
                "Policy evaluation: tool '{ToolKey}' requires review (high risk) for profile '{ProfileId}'",
                toolKey, agentProfileId);
            return PolicyDecision.AllowWithReview($"High-risk tool '{tool.DisplayName}' requires review before execution.");
        }

        // Medium risk: always requires review
        if (tool.RiskLevel == ToolRiskLevel.Medium)
        {
            _logger?.LogInformation(
                "Policy evaluation: tool '{ToolKey}' requires review (medium risk) for profile '{ProfileId}'",
                toolKey, agentProfileId);
            return PolicyDecision.AllowWithReview($"Medium-risk tool '{tool.DisplayName}' requires review before execution.");
        }

        // Low risk: review-first by default; direct apply only if explicitly enabled
        if (policy.AutoApplyLowRisk)
        {
            _logger?.LogInformation(
                "Policy evaluation: tool '{ToolKey}' allowed direct (low risk, auto-apply enabled) for profile '{ProfileId}'",
                toolKey, agentProfileId);
            return PolicyDecision.AllowDirect($"Low-risk tool '{tool.DisplayName}' auto-applied per policy.");
        }

        _logger?.LogInformation(
            "Policy evaluation: tool '{ToolKey}' requires review (low risk, auto-apply off) for profile '{ProfileId}'",
            toolKey, agentProfileId);
        return PolicyDecision.AllowWithReview($"Low-risk tool '{tool.DisplayName}' requires review (auto-apply is off).");
    }

    /// <summary>
    /// Parse the profile's PolicyJson into a structured policy configuration.
    /// Returns safe defaults if the JSON is missing or malformed.
    /// </summary>
    internal static AgentPolicyConfig ParsePolicy(string? policyJson)
    {
        if (string.IsNullOrWhiteSpace(policyJson) || policyJson == "{}")
            return AgentPolicyConfig.Default;

        try
        {
            using var doc = JsonDocument.Parse(policyJson);
            var root = doc.RootElement;

            var allowedTools = new List<string>();
            if (root.TryGetProperty("allowedTools", out var toolsElement) && toolsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in toolsElement.EnumerateArray())
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        allowedTools.Add(value);
                }
            }

            var autoApplyLowRisk = false;
            if (root.TryGetProperty("autoApplyLowRisk", out var autoApplyElement)
                && autoApplyElement.ValueKind == JsonValueKind.True)
            {
                autoApplyLowRisk = true;
            }

            return new AgentPolicyConfig(allowedTools, autoApplyLowRisk);
        }
        catch (JsonException)
        {
            return AgentPolicyConfig.Default;
        }
    }
}

/// <summary>
/// Parsed representation of the agent profile's policy configuration.
/// </summary>
internal sealed record AgentPolicyConfig(
    IReadOnlyList<string> AllowedTools,
    bool AutoApplyLowRisk)
{
    public static AgentPolicyConfig Default { get; } = new(Array.Empty<string>(), false);
}
