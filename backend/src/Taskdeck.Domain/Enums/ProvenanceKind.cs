namespace Taskdeck.Domain.Enums;

/// <summary>
/// Indicates how a provenance field was derived from source material.
/// </summary>
public enum ProvenanceKind
{
    /// <summary>
    /// Field value was extracted directly (verbatim or near-verbatim) from source text.
    /// </summary>
    Extractive,

    /// <summary>
    /// Field value was inferred, synthesized, or transformed from source material.
    /// </summary>
    Inferred
}
