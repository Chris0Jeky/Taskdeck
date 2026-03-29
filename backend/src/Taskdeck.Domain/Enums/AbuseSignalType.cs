namespace Taskdeck.Domain.Enums;

/// <summary>
/// Types of abuse signals detected for managed-key LLM traffic.
/// </summary>
public enum AbuseSignalType
{
    /// <summary>Request or token velocity significantly above normal for the time window.</summary>
    AnomalousVelocity = 0,

    /// <summary>Repeated blocked or refused content patterns from the LLM provider.</summary>
    RepeatedBlockedContent = 1,

    /// <summary>Repeated attempts to circumvent rate limits or quota enforcement.</summary>
    LimitHitEvasion = 2,

    /// <summary>Suspicious concentration of requests from a single account or scope.</summary>
    SuspiciousConcentration = 3,

    /// <summary>Operator manually escalated the actor's abuse state.</summary>
    ManualEscalation = 4,

    /// <summary>Operator manually de-escalated or cleared the actor's abuse state.</summary>
    ManualOverride = 5
}
