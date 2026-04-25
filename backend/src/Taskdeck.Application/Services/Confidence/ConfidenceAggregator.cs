using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Confidence;

/// <summary>
/// Default implementation of <see cref="IConfidenceAggregator"/> that performs
/// weighted averaging of confidence scores from multiple sources.
/// </summary>
public sealed class ConfidenceAggregator : IConfidenceAggregator
{
    /// <inheritdoc />
    public AggregatedConfidence? Aggregate(
        IReadOnlyList<ConfidenceScore> scores,
        IReadOnlyDictionary<ConfidenceSource, double>? weights = null)
    {
        if (scores is null || scores.Count == 0)
            return null;

        ValidateWeights(weights);

        var useWeights = weights is not null && weights.Count > 0;
        double weightedSum = 0.0;
        double totalWeight = 0.0;
        var contributing = new List<ConfidenceScore>();

        foreach (var score in scores)
        {
            if (score is null)
                throw new DomainException(ErrorCodes.ValidationError,
                    "Confidence score entries cannot be null.");

            double w;
            if (useWeights)
            {
                if (!weights!.TryGetValue(score.Source, out w))
                    w = 0.0; // Missing source in weight map → excluded
            }
            else
            {
                w = 1.0; // Equal weighting
            }

            if (w <= 0.0)
                continue; // Skip zero-weight sources

            weightedSum += score.Score * w;
            totalWeight += w;
            contributing.Add(score);
        }

        if (totalWeight <= 0.0 || contributing.Count == 0)
            return null; // All weights were zero or no valid scores

        var aggregated = weightedSum / totalWeight;

        // Clamp to [0.0, 1.0] for safety against floating-point drift
        aggregated = Math.Clamp(aggregated, 0.0, 1.0);

        var bucket = ConfidenceScore.ScoreToBucket(aggregated);

        return new AggregatedConfidence(aggregated, bucket, contributing.AsReadOnly());
    }

    /// <inheritdoc />
    public FieldConfidence AggregateForField(
        string fieldName,
        IReadOnlyList<ConfidenceScore> scores,
        IReadOnlyDictionary<ConfidenceSource, double>? weights = null)
    {
        var result = Aggregate(scores, weights);

        if (result is null)
        {
            // No valid aggregation possible — use score 0.0 with empty breakdown
            return new FieldConfidence(fieldName, 0.0, Array.Empty<ConfidenceScore>());
        }

        return new FieldConfidence(fieldName, result.Score, result.ContributingScores);
    }

    private static void ValidateWeights(IReadOnlyDictionary<ConfidenceSource, double>? weights)
    {
        if (weights is null)
            return;

        foreach (var kvp in weights)
        {
            if (kvp.Value < 0.0)
                throw new DomainException(ErrorCodes.ValidationError,
                    $"Weight for source {kvp.Key} cannot be negative ({kvp.Value}).");

            if (double.IsNaN(kvp.Value) || double.IsInfinity(kvp.Value))
                throw new DomainException(ErrorCodes.ValidationError,
                    $"Weight for source {kvp.Key} must be a finite number.");
        }
    }
}
