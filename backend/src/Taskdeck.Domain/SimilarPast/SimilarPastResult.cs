namespace Taskdeck.Domain.SimilarPast;

/// <summary>
/// Aggregated result of similar past decisions for a proposal, including
/// the top N most recent decisions and the overall apply rate.
/// </summary>
/// <param name="Decisions">The most recent similar decisions (up to 3).</param>
/// <param name="ApplyRate">
/// Ratio of applied to total terminal decisions (applied + rejected).
/// 0.0 when there is no history.
/// </param>
public sealed record SimilarPastResult(
    IReadOnlyList<SimilarPastDecision> Decisions,
    double ApplyRate)
{
    /// <summary>
    /// An empty result representing no prior history.
    /// </summary>
    public static SimilarPastResult Empty { get; } =
        new(Array.Empty<SimilarPastDecision>(), 0.0);

    /// <summary>
    /// Computes the apply rate from applied and rejected counts,
    /// returning 0.0 when the denominator is zero.
    /// </summary>
    public static double ComputeApplyRate(int appliedCount, int rejectedCount)
    {
        if (appliedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(appliedCount), "Applied count cannot be negative.");
        if (rejectedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(rejectedCount), "Rejected count cannot be negative.");

        var total = appliedCount + rejectedCount;
        return total == 0 ? 0.0 : (double)appliedCount / total;
    }
}
