namespace Taskdeck.Domain.Agents;

/// <summary>
/// Types of egress violations that can occur when an agent attempts
/// to reach an external host not in the egress envelope.
/// </summary>
public enum EgressViolationType
{
    /// <summary>The target host is not registered in the egress registry.</summary>
    UnknownHost = 0,

    /// <summary>A redirect response points to a host not in the egress registry.</summary>
    RedirectToUnknownHost = 1,

    /// <summary>The target address resolves to a private/loopback network (SSRF attempt).</summary>
    PrivateNetworkAttempt = 2
}

/// <summary>
/// Value object describing an egress violation — an attempt by an agent or component
/// to reach an external host that is not in the approved egress envelope.
/// Immutable by design. Carries enough context for audit logging without user content.
/// </summary>
public sealed class EgressViolation
{
    public string AttemptedHost { get; }
    public string RequestUri { get; }
    public EgressViolationType ViolationType { get; }
    public string Reason { get; }
    public string? SourceComponent { get; }
    public DateTimeOffset DetectedAt { get; }

    public EgressViolation(
        string attemptedHost,
        string requestUri,
        EgressViolationType violationType,
        string reason,
        string? sourceComponent = null)
    {
        if (string.IsNullOrWhiteSpace(attemptedHost))
            throw new ArgumentException("AttemptedHost cannot be empty.", nameof(attemptedHost));

        if (string.IsNullOrWhiteSpace(requestUri))
            throw new ArgumentException("RequestUri cannot be empty.", nameof(requestUri));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be empty.", nameof(reason));

        AttemptedHost = attemptedHost;
        RequestUri = requestUri;
        ViolationType = violationType;
        Reason = reason;
        SourceComponent = sourceComponent;
        DetectedAt = DateTimeOffset.UtcNow;
    }

    public override string ToString()
        => $"EgressViolation({ViolationType}: {AttemptedHost} — {Reason})";
}
