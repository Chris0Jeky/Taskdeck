namespace Taskdeck.Domain.Enums;

/// <summary>
/// Result status of verifying a provenance field against its source material.
/// </summary>
public enum VerificationStatus
{
    /// <summary>
    /// The field was verified as matching the source material.
    /// </summary>
    Verified,

    /// <summary>
    /// The field has not yet been verified.
    /// </summary>
    Unverified,

    /// <summary>
    /// Verification was attempted but failed (source not found, no match).
    /// </summary>
    Failed,

    /// <summary>
    /// The field was partially verified but confidence was downgraded
    /// (e.g., fuzzy match below threshold).
    /// </summary>
    Downgraded
}
