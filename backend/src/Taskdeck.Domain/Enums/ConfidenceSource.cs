namespace Taskdeck.Domain.Enums;

/// <summary>
/// The origin of a confidence signal used to build an aggregated confidence score.
/// </summary>
public enum ConfidenceSource
{
    /// <summary>
    /// Confidence extracted from the LLM's natural-language self-assessment.
    /// </summary>
    Verbalized = 0,

    /// <summary>
    /// Confidence derived from provider-reported token log-probabilities.
    /// </summary>
    ProviderLogprob = 1,

    /// <summary>
    /// Confidence from provenance verification (source traceability checks).
    /// </summary>
    ProvenanceVerification = 2,

    /// <summary>
    /// Confidence from self-consistency (multiple independent generations compared).
    /// </summary>
    SelfConsistency = 3
}
