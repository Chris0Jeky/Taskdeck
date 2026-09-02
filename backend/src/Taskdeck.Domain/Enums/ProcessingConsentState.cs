namespace Taskdeck.Domain.Enums;

/// <summary>
/// Lifecycle of a one-time remote-processing consent (ADR-0065 ruling 5; CF-10 <c>#2264</c>).
/// Consent is recorded once per destination and data class and is revocable; it is re-checked when a
/// job is <b>claimed</b>, not only when it was enqueued, so a revocation between the two makes the
/// remote processor ineligible on that job. Grants are never edited in place: a change supersedes.
/// </summary>
public enum ProcessingConsentState
{
    /// <summary>In force; the destination and data class it names are eligible for routing.</summary>
    Active = 0,

    /// <summary>Withdrawn by the owner; no new job may route to the destination it named.</summary>
    Revoked = 1,

    /// <summary>Past its expiry instant; treated exactly like <see cref="Revoked"/> at claim time.</summary>
    Expired = 2,

    /// <summary>Replaced by a later grant for the same destination and data class; kept for the receipt trail.</summary>
    Superseded = 3
}
