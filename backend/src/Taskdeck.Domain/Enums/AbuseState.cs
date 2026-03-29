namespace Taskdeck.Domain.Enums;

/// <summary>
/// Deterministic abuse state model for managed-key LLM traffic.
/// Transitions follow: Observe → Suspicious → Restricted → Blocked.
/// All transitions are reversible with operator override.
/// </summary>
public enum AbuseState
{
    /// <summary>Normal monitoring, no abuse signals detected.</summary>
    Observe = 0,

    /// <summary>Abuse signals detected but below containment threshold. Stricter throttles applied.</summary>
    Suspicious = 1,

    /// <summary>Abuse threshold exceeded. Provider calls temporarily disabled for actor/scope.</summary>
    Restricted = 2,

    /// <summary>Severe or repeated abuse. Actor fully blocked. Mandatory manual review required.</summary>
    Blocked = 3
}
