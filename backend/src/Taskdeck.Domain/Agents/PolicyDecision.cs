namespace Taskdeck.Domain.Agents;

/// <summary>
/// Value object representing the outcome of a policy evaluation for a tool use request.
/// Immutable by design — once produced by the policy evaluator, a decision is final.
/// </summary>
public sealed class PolicyDecision
{
    /// <summary>Whether the tool invocation is allowed to proceed.</summary>
    public bool Allowed { get; }

    /// <summary>Whether the result must be routed through the review gate before applying.</summary>
    public bool RequiresReview { get; }

    /// <summary>Human-readable reason for the decision, suitable for audit trails.</summary>
    public string Reason { get; }

    private PolicyDecision(bool allowed, bool requiresReview, string reason)
    {
        Allowed = allowed;
        RequiresReview = requiresReview;
        Reason = reason;
    }

    /// <summary>Create a decision allowing execution but requiring proposal review.</summary>
    public static PolicyDecision AllowWithReview(string reason)
        => new(true, true, reason);

    /// <summary>Create a decision denying execution entirely.</summary>
    public static PolicyDecision Deny(string reason)
        => new(false, false, reason);

    /// <summary>Create a decision allowing direct execution (low-risk, auto-apply enabled).</summary>
    public static PolicyDecision AllowDirect(string reason)
        => new(true, false, reason);

    public override bool Equals(object? obj)
    {
        if (obj is not PolicyDecision other) return false;
        return Allowed == other.Allowed
            && RequiresReview == other.RequiresReview
            && Reason == other.Reason;
    }

    public override int GetHashCode()
        => HashCode.Combine(Allowed, RequiresReview, Reason);

    public override string ToString()
        => $"PolicyDecision(Allowed={Allowed}, RequiresReview={RequiresReview}, Reason=\"{Reason}\")";
}
