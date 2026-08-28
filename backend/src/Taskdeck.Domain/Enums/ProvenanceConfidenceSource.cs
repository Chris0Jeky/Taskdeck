namespace Taskdeck.Domain.Enums;

/// <summary>
/// Identifies where a provenance confidence value came from. The source is persisted separately
/// from the nullable value so deterministic extraction can be represented without inventing a
/// number and historical/unknown records can fail closed to "not reported".
/// </summary>
public enum ProvenanceConfidenceSource
{
    /// <summary>No trustworthy confidence source was recorded.</summary>
    NotReported = 0,

    /// <summary>The model reported this value for the corresponding extracted item.</summary>
    ModelReported = 1,

    /// <summary>A deterministic extractor produced the item and therefore no model value exists.</summary>
    Deterministic = 2,

    /// <summary>A non-model verification or matching algorithm derived the value.</summary>
    Derived = 3
}
