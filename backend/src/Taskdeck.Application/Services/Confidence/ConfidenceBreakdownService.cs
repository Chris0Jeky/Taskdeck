using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Confidence;

/// <summary>
/// Computes a multi-component confidence breakdown for a proposal.
/// Default components: Pattern match, Reach, Reversibility, Recency.
/// </summary>
public sealed class ConfidenceBreakdownService : IConfidenceBreakdownService
{
    /// <summary>
    /// Default apply threshold. Proposals at or above this value are considered high-confidence.
    /// </summary>
    internal const double DefaultThreshold = 0.7;

    /// <summary>
    /// Component key constants used in breakdown computation.
    /// </summary>
    internal static class ComponentKeys
    {
        public const string PatternMatch = "Pattern match";
        public const string Reach = "Reach";
        public const string Reversibility = "Reversibility";
        public const string Recency = "Recency";
    }

    /// <summary>
    /// Action types considered destructive (lower reversibility).
    /// </summary>
    private static readonly HashSet<string> DestructiveActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "archive", "remove"
    };

    /// <summary>
    /// Action types considered safe / easily reversible (higher reversibility).
    /// </summary>
    private static readonly HashSet<string> SafeActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "add", "set", "update", "rename"
    };

    /// <summary>
    /// Action types that are known patterns for well-understood instructions.
    /// </summary>
    private static readonly HashSet<string> WellKnownActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "move", "update", "delete", "archive", "add", "remove",
        "rename", "set", "restore", "unarchive", "block", "unblock",
        "assign", "attach", "reorder", "apply"
    };

    private readonly IUnitOfWork _unitOfWork;

    public ConfidenceBreakdownService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<Result<ConfidenceBreakdownDto>> GetBreakdownAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal is null)
            return Result.Failure<ConfidenceBreakdownDto>(ErrorCodes.NotFound,
                $"Proposal {proposalId} not found.");

        var components = ComputeComponents(proposal);
        var overall = ComputeOverall(components);
        var note = GenerateNote(overall, DefaultThreshold);

        var breakdown = new ConfidenceBreakdown(overall, components, note, DefaultThreshold);

        var dto = new ConfidenceBreakdownDto(
            breakdown.Overall,
            breakdown.Components.Select(c => new ConfidenceComponentDto(c.Key, c.Value)).ToList(),
            breakdown.Note,
            breakdown.Threshold);

        return Result.Success(dto);
    }

    /// <summary>
    /// Computes the four default confidence components from proposal metadata.
    /// </summary>
    internal static IReadOnlyList<ConfidenceComponent> ComputeComponents(AutomationProposal proposal)
    {
        return new[]
        {
            new ConfidenceComponent(ComponentKeys.PatternMatch, ComputePatternMatch(proposal)),
            new ConfidenceComponent(ComponentKeys.Reach, ComputeReach(proposal)),
            new ConfidenceComponent(ComponentKeys.Reversibility, ComputeReversibility(proposal)),
            new ConfidenceComponent(ComponentKeys.Recency, ComputeRecency(proposal))
        };
    }

    /// <summary>
    /// Pattern match: how well the instruction matched known action patterns.
    /// Well-known action types score high; unknown ones score lower.
    /// </summary>
    internal static double ComputePatternMatch(AutomationProposal proposal)
    {
        var operations = proposal.Operations;
        if (operations.Count == 0)
            return 0.5; // No operations = neutral confidence

        int matched = 0;
        foreach (var op in operations)
        {
            if (WellKnownActions.Contains(op.ActionType))
                matched++;
        }

        return (double)matched / operations.Count;
    }

    /// <summary>
    /// Reach: how many entities are affected. Single-entity proposals score high (focused);
    /// multi-entity proposals score lower (broader blast radius).
    /// Score = 1.0 / (1.0 + log2(distinctTargets))
    /// </summary>
    internal static double ComputeReach(AutomationProposal proposal)
    {
        var operations = proposal.Operations;
        if (operations.Count == 0)
            return 1.0; // No operations = no blast radius

        var distinctTargets = operations
            .Where(op => !string.IsNullOrEmpty(op.TargetId))
            .Select(op => op.TargetId)
            .Distinct()
            .Count();

        // If no target IDs, use operation count as a proxy
        if (distinctTargets == 0)
            distinctTargets = operations.Count;

        // Score inversely proportional to the number of affected entities.
        // 1 target => 1.0, 2 targets => ~0.67, 4 targets => ~0.5, 8 targets => ~0.4
        var score = 1.0 / (1.0 + Math.Log2(distinctTargets));
        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Reversibility: inverse of operation risk level.
    /// Destructive actions score low; safe actions score high.
    /// </summary>
    internal static double ComputeReversibility(AutomationProposal proposal)
    {
        // Start from proposal-level risk
        double riskBaseline = proposal.RiskLevel switch
        {
            RiskLevel.Low => 0.9,
            RiskLevel.Medium => 0.7,
            RiskLevel.High => 0.4,
            RiskLevel.Critical => 0.1,
            _ => 0.5
        };

        var operations = proposal.Operations;
        if (operations.Count == 0)
            return riskBaseline;

        // Adjust based on action types
        int destructive = 0;
        int safe = 0;
        foreach (var op in operations)
        {
            if (DestructiveActions.Contains(op.ActionType))
                destructive++;
            else if (SafeActions.Contains(op.ActionType))
                safe++;
        }

        double actionFactor;
        if (destructive > 0 && safe == 0)
            actionFactor = 0.3;
        else if (safe > 0 && destructive == 0)
            actionFactor = 0.9;
        else if (destructive > 0 && safe > 0)
            actionFactor = 0.5;
        else
            actionFactor = 0.6; // Unknown actions

        // Blend: 60% risk baseline, 40% action-type factor
        var score = (riskBaseline * 0.6) + (actionFactor * 0.4);
        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Recency: how fresh the proposal is relative to its expiry window.
    /// A freshly created proposal scores 1.0; one near expiry scores close to 0.0.
    /// </summary>
    internal static double ComputeRecency(AutomationProposal proposal)
    {
        var now = DateTime.UtcNow;
        var created = proposal.CreatedAt.UtcDateTime;
        var expires = proposal.ExpiresAt;

        // Guard: if expiry is at or before creation, return 0 (degenerate case)
        var totalWindow = (expires - created).TotalSeconds;
        if (totalWindow <= 0.0)
            return 0.0;

        var elapsed = (now - created).TotalSeconds;

        // If proposal hasn't been created yet (clock skew) or is in the future
        if (elapsed < 0.0)
            return 1.0;

        var remaining = Math.Max(0.0, totalWindow - elapsed);
        var score = remaining / totalWindow;
        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Computes the overall score as a weighted average of the four components.
    /// Weights: PatternMatch=0.3, Reach=0.2, Reversibility=0.35, Recency=0.15
    /// </summary>
    internal static double ComputeOverall(IReadOnlyList<ConfidenceComponent> components)
    {
        if (components.Count == 0)
            return 0.0;

        // Component weights (must sum to 1.0)
        var weights = new Dictionary<string, double>
        {
            [ComponentKeys.PatternMatch] = 0.30,
            [ComponentKeys.Reach] = 0.20,
            [ComponentKeys.Reversibility] = 0.35,
            [ComponentKeys.Recency] = 0.15
        };

        double weightedSum = 0.0;
        double totalWeight = 0.0;

        foreach (var component in components)
        {
            if (weights.TryGetValue(component.Key, out var weight))
            {
                weightedSum += component.Value * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0.0)
            return 0.0;

        var overall = weightedSum / totalWeight;
        return Math.Clamp(overall, 0.0, 1.0);
    }

    /// <summary>
    /// Generates an explanatory note when the overall score is near the threshold.
    /// </summary>
    internal static string? GenerateNote(double overall, double threshold)
    {
        const double nearThresholdBand = 0.1;

        if (overall >= threshold)
        {
            if (overall - threshold < nearThresholdBand)
                return $"Confidence is just above the threshold ({threshold:P0}). Review recommended.";
            return null; // Clearly above threshold, no note needed
        }

        // Below threshold
        if (threshold - overall < nearThresholdBand)
            return $"Confidence is just below the threshold ({threshold:P0}). Close to auto-apply eligibility.";

        return $"Confidence is below the threshold ({threshold:P0}). Manual review required.";
    }
}
