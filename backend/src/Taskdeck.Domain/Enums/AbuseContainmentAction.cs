namespace Taskdeck.Domain.Enums;

/// <summary>
/// Automated containment actions applied per abuse state.
/// </summary>
public enum AbuseContainmentAction
{
    /// <summary>No containment active.</summary>
    None = 0,

    /// <summary>Stricter request throttles applied to the actor.</summary>
    StricterThrottles = 1,

    /// <summary>Temporary user lock — LLM provider calls disabled for the actor.</summary>
    TemporaryLock = 2,

    /// <summary>Provider calls fully disabled for the actor/scope.</summary>
    ProviderCallsDisabled = 3,

    /// <summary>Mandatory manual review required before actor can resume.</summary>
    MandatoryManualReview = 4
}
