namespace Taskdeck.Application.Services;

/// <summary>
/// A content-free insight metric for privacy-preserving analytics.
/// All fields are numeric or categorical -- no string fields that could
/// contain user-generated text or PII.
///
/// Design invariant: this record must NEVER include fields of type string
/// that could carry user content (task titles, descriptions, comments, etc.).
/// The MetricName and PromptVersion fields are constrained to a closed set
/// of system-defined identifiers, not user-supplied text.
/// </summary>
/// <param name="MetricName">System-defined metric identifier (e.g. "proposal.acceptance_rate"). Not user content.</param>
/// <param name="BucketedCount">Quantized count to prevent fingerprinting individual users.</param>
/// <param name="TimePeriodDays">Aggregation window in days (e.g. 7, 30).</param>
/// <param name="PromptVersion">System-defined prompt version identifier (e.g. "v2.1"). Not user content.</param>
public sealed record InsightMetric(
    string MetricName,
    int BucketedCount,
    int TimePeriodDays,
    string PromptVersion)
{
    /// <summary>
    /// Validates that the metric name is not empty and bucketed count is non-negative.
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(MetricName)
            && BucketedCount >= 0
            && TimePeriodDays > 0
            && !string.IsNullOrWhiteSpace(PromptVersion);
    }
}

/// <summary>
/// Aggregated cohort statistics for proposal review outcomes.
/// Content-free by design: only acceptance/edit/reject counts are tracked.
/// No user content, task text, or identifiers are included.
///
/// Design invariant: this record must NEVER include fields that carry
/// user-generated content. Only aggregate numeric counts.
/// </summary>
/// <param name="AcceptedCount">Number of proposals accepted without edits.</param>
/// <param name="EditedCount">Number of proposals accepted with edits.</param>
/// <param name="RejectedCount">Number of proposals rejected.</param>
public sealed record InsightCohort(
    int AcceptedCount,
    int EditedCount,
    int RejectedCount)
{
    /// <summary>Total proposals reviewed in this cohort.</summary>
    public int TotalCount => AcceptedCount + EditedCount + RejectedCount;

    /// <summary>
    /// Acceptance rate as a fraction [0.0, 1.0]. Returns 0 if no proposals reviewed.
    /// </summary>
    public double AcceptanceRate => TotalCount > 0 ? (double)AcceptedCount / TotalCount : 0.0;
}
