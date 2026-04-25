using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Confidence;

/// <summary>
/// Immutable value object representing a single confidence signal from a specific source.
/// Score is clamped to [0.0, 1.0].
/// </summary>
public sealed class ConfidenceScore : IEquatable<ConfidenceScore>, IComparable<ConfidenceScore>
{
    private const double Epsilon = 1e-12;

    /// <summary>
    /// The confidence value in [0.0, 1.0].
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// The origin of this confidence signal.
    /// </summary>
    public ConfidenceSource Source { get; }

    /// <summary>
    /// Human-readable explanation of why this score was assigned.
    /// </summary>
    public string Explanation { get; }

    public ConfidenceScore(double score, ConfidenceSource source, string explanation)
    {
        if (double.IsNaN(score) || double.IsInfinity(score))
            throw new DomainException(ErrorCodes.ValidationError,
                "Confidence score must be a finite number.");

        if (score < 0.0 || score > 1.0)
            throw new DomainException(ErrorCodes.ValidationError,
                $"Confidence score must be between 0.0 and 1.0, but was {score}.");

        Score = score;
        Source = source;
        Explanation = explanation ?? string.Empty;
    }

    /// <summary>
    /// Returns the <see cref="ConfidenceBucket"/> for this score.
    /// Boundaries: [0, 0.2) = VeryLow, [0.2, 0.4) = Low, [0.4, 0.6) = Medium,
    /// [0.6, 0.8) = High, [0.8, 1.0] = VeryHigh.
    /// </summary>
    public ConfidenceBucket ToBucket() => ScoreToBucket(Score);

    /// <summary>
    /// Static helper to convert any score in [0.0, 1.0] to a <see cref="ConfidenceBucket"/>.
    /// </summary>
    public static ConfidenceBucket ScoreToBucket(double score)
    {
        if (double.IsNaN(score) || double.IsInfinity(score))
            throw new DomainException(ErrorCodes.ValidationError,
                "Confidence score must be a finite number.");

        if (score < 0.0 || score > 1.0)
            throw new DomainException(ErrorCodes.ValidationError,
                $"Confidence score must be between 0.0 and 1.0, but was {score}.");

        return score switch
        {
            < 0.2 => ConfidenceBucket.VeryLow,
            < 0.4 => ConfidenceBucket.Low,
            < 0.6 => ConfidenceBucket.Medium,
            < 0.8 => ConfidenceBucket.High,
            _ => ConfidenceBucket.VeryHigh   // [0.8, 1.0]
        };
    }

    #region Equality & Comparison

    public bool Equals(ConfidenceScore? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        // Use epsilon comparison for floating-point equality
        return Math.Abs(Score - other.Score) < Epsilon
               && Source == other.Source
               && Explanation == other.Explanation;
    }

    public override bool Equals(object? obj) => Equals(obj as ConfidenceScore);

    public override int GetHashCode()
    {
        // Score participates in Equals with epsilon tolerance, so hashing its raw
        // value or a rounded bucket can still split equal values at bucket edges.
        return HashCode.Combine(Source, Explanation);
    }

    public int CompareTo(ConfidenceScore? other)
    {
        if (other is null) return 1;
        if (Equals(other)) return 0;

        var scoreComparison = Score.CompareTo(other.Score);
        if (scoreComparison != 0 && Math.Abs(Score - other.Score) >= Epsilon)
            return scoreComparison;

        var sourceComparison = Source.CompareTo(other.Source);
        if (sourceComparison != 0)
            return sourceComparison;

        return string.CompareOrdinal(Explanation, other.Explanation);
    }

    public static bool operator ==(ConfidenceScore? left, ConfidenceScore? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(ConfidenceScore? left, ConfidenceScore? right) => !(left == right);

    #endregion

    public override string ToString() => $"[{Source}] {Score:F3} – {Explanation}";
}
