namespace Taskdeck.Domain.Enums;

/// <summary>
/// Lifecycle of a one-time remote-processing consent (ADR-0065 ruling 5; CF-10 <c>#2264</c>).
/// Consent is recorded once per destination and data class and is revocable. CF-10 will re-check it
/// when a job is <b>claimed</b>, not only when it was enqueued, so a revocation between the two makes
/// the remote processor ineligible on that job; grants are never edited in place, a change supersedes.
/// <b>Not implemented yet:</b> no consent record, store or check exists — this is vocabulary scaffolded
/// ahead of CF-10. The zero value fails closed: a row whose state was never written reads as revoked.
/// </summary>
public enum ProcessingConsentState
{
    /// <summary>Withdrawn by the owner, or never recorded (the default); no new job may route to the destination it names.</summary>
    Revoked = 0,

    /// <summary>Past its expiry instant; treated exactly like <see cref="Revoked"/> at claim time.</summary>
    Expired = 1,

    /// <summary>Replaced by a later grant for the same destination and data class; kept for the receipt trail.</summary>
    Superseded = 2,

    /// <summary>In force; the destination and data class it names are eligible for routing.</summary>
    Active = 3
}
