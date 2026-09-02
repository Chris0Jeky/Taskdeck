namespace Taskdeck.Domain.Enums;

/// <summary>
/// Whether a Context Fabric runtime metric can honestly be reported (ADR-0065 §Decision 8; CF-24B
/// <c>#2277</c>). A metric is a ratio over a defined denominator; when the denominator is too small,
/// absent or unknowable the report says so instead of showing a number, because these figures feed
/// router scoring and the CF-22 evidence bar and must never inflate confidence.
/// </summary>
public enum MetricAvailability
{
    /// <summary>The denominator meets the metric's minimum cohort and every input fact is known.</summary>
    Available = 0,

    /// <summary>Facts exist but the cohort is below the metric's minimum; no percentage is shown.</summary>
    InsufficientCohort = 1,

    /// <summary>The denominator is not yet recorded anywhere (for example cost before run-linked usage exists).</summary>
    NoDenominator = 2,

    /// <summary>An input fact is missing or ambiguous for this window; the metric is reported as unknown, never as zero.</summary>
    Unknown = 3
}
