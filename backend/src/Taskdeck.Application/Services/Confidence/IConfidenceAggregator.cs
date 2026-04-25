using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services.Confidence;

/// <summary>
/// Combines multiple <see cref="ConfidenceScore"/> inputs from different sources
/// into a single aggregated score with configurable per-source weights.
/// </summary>
public interface IConfidenceAggregator
{
    /// <summary>
    /// Aggregates multiple confidence scores into a single score using weighted combination.
    /// Missing sources are handled gracefully (excluded from aggregation).
    /// </summary>
    /// <param name="scores">The individual confidence scores to aggregate.</param>
    /// <param name="weights">
    /// Optional per-source weights. If null or empty, equal weighting is used.
    /// Weights must be non-negative. Sources with zero weight are excluded.
    /// </param>
    /// <returns>
    /// The aggregated score in [0.0, 1.0] and its bucket, or null if no valid inputs exist.
    /// </returns>
    AggregatedConfidence? Aggregate(
        IReadOnlyList<ConfidenceScore> scores,
        IReadOnlyDictionary<ConfidenceSource, double>? weights = null);

    /// <summary>
    /// Builds a <see cref="FieldConfidence"/> by aggregating scores for a specific proposal field.
    /// </summary>
    FieldConfidence AggregateForField(
        string fieldName,
        IReadOnlyList<ConfidenceScore> scores,
        IReadOnlyDictionary<ConfidenceSource, double>? weights = null);
}

/// <summary>
/// Result of aggregating multiple confidence signals.
/// </summary>
public sealed class AggregatedConfidence
{
    public double Score { get; }
    public ConfidenceBucket Bucket { get; }
    public IReadOnlyList<ConfidenceScore> ContributingScores { get; }

    public AggregatedConfidence(double score, ConfidenceBucket bucket, IReadOnlyList<ConfidenceScore> contributingScores)
    {
        Score = score;
        Bucket = bucket;
        ContributingScores = contributingScores;
    }
}
