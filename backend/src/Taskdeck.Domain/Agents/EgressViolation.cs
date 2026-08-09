namespace Taskdeck.Domain.Agents;

/// <summary>
/// Value object representing a detected egress policy violation.
/// Created when an agent or tool attempts to contact an unauthorized host
/// or when a redirect leads to an out-of-envelope destination.
/// GP-10: violations must be loud, never swallowed.
/// </summary>
public sealed class EgressViolation
{
    /// <summary>The host that was attempted but is not in the egress envelope.</summary>
    public string AttemptedHost { get; }

    /// <summary>The sanitized audit origin for the request that triggered the violation.</summary>
    public string RequestUri { get; }

    /// <summary>Category of the violation.</summary>
    public EgressViolationType ViolationType { get; }

    /// <summary>Human-readable explanation of why this was blocked.</summary>
    public string Reason { get; }

    /// <summary>When the violation was detected (UTC).</summary>
    public DateTimeOffset DetectedAt { get; }

    /// <summary>The tool or agent name that initiated the request, if known.</summary>
    public string? SourceComponent { get; }

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

/// <summary>
/// Categories of egress violations.
/// </summary>
public enum EgressViolationType
{
    /// <summary>The host is not in the egress registry allowlist.</summary>
    UnknownHost = 0,

    /// <summary>An HTTP redirect led to an out-of-envelope host.</summary>
    RedirectToUnknownHost = 1,

    /// <summary>The host resolved to a private/internal IP range.</summary>
    PrivateNetworkAttempt = 2,

    /// <summary>The client policy refuses redirects even when the target host is allowed.</summary>
    RedirectNotAllowed = 3
}
