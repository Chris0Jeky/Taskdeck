using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Confidence;

/// <summary>
/// Links a proposal field name to its aggregated confidence score
/// with a per-source breakdown of contributing signals.
/// </summary>
public sealed class FieldConfidence
{
    /// <summary>
    /// The name of the proposal field this confidence applies to (e.g., "title", "column", "description").
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// The overall aggregated confidence score for this field.
    /// </summary>
    public double AggregatedScore { get; }

    /// <summary>
    /// The bucket derived from the aggregated score.
    /// </summary>
    public ConfidenceBucket Bucket { get; }

    /// <summary>
    /// Per-source breakdown of confidence signals that contributed to the aggregated score.
    /// </summary>
    public IReadOnlyList<ConfidenceScore> SourceBreakdown { get; }

    public FieldConfidence(string fieldName, double aggregatedScore, IReadOnlyList<ConfidenceScore> sourceBreakdown)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new DomainException(ErrorCodes.ValidationError,
                "Field name cannot be empty.");

        if (double.IsNaN(aggregatedScore) || double.IsInfinity(aggregatedScore))
            throw new DomainException(ErrorCodes.ValidationError,
                "Aggregated score must be a finite number.");

        if (aggregatedScore < 0.0 || aggregatedScore > 1.0)
            throw new DomainException(ErrorCodes.ValidationError,
                $"Aggregated score must be between 0.0 and 1.0, but was {aggregatedScore}.");

        FieldName = fieldName;
        AggregatedScore = aggregatedScore;
        Bucket = ConfidenceScore.ScoreToBucket(aggregatedScore);
        SourceBreakdown = sourceBreakdown ?? Array.Empty<ConfidenceScore>();
    }

    public override string ToString() =>
        $"{FieldName}: {AggregatedScore:F3} ({Bucket}), {SourceBreakdown.Count} source(s)";
}
