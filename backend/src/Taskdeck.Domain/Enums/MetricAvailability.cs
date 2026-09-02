namespace Taskdeck.Domain.Enums;

/// <summary>
/// Whether a Context Fabric runtime metric can honestly be reported (ADR-0065 §Decision 8; CF-24B
/// <c>#2277</c>). A metric is a ratio over a defined denominator; when the denominator is too small,
/// absent or unknowable the report says so instead of showing a number, because these figures feed
/// router scoring and the CF-22 evidence bar and must never inflate confidence. <b>Not implemented
/// yet:</b> no metric fact, projection or report exists — this is vocabulary scaffolded ahead of CF-24B.
/// The zero value fails closed: a metric whose availability was never computed reads as unknown.
/// </summary>
public enum MetricAvailability
{
    /// <summary>An input fact is missing or ambiguous for this window, or availability was never computed (the default); reported as unknown, never as zero.</summary>
    Unknown = 0,

    /// <summary>The denominator is not yet recorded anywhere (for example cost before run-linked priced usage exists).</summary>
    NoDenominator = 1,

    /// <summary>Facts exist but the cohort is below the metric's minimum; no percentage is shown.</summary>
    InsufficientCohort = 2,

    /// <summary>The denominator meets the metric's minimum cohort and every input fact is known.</summary>
    Available = 3
}
