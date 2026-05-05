using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Confidence;

/// <summary>
/// Immutable value object representing a multi-component confidence breakdown
/// for a proposal. Exposes the overall score, per-component scores, an optional
/// explanatory note, and the apply threshold so the UI can explain why a proposal
/// is or isn't above the auto-apply threshold.
/// </summary>
public sealed class ConfidenceBreakdown : IEquatable<ConfidenceBreakdown>
{
    /// <summary>
    /// The overall confidence score in [0.0, 1.0].
    /// </summary>
    public double Overall { get; }

    /// <summary>
    /// Per-component breakdown (e.g. Pattern match, Reach, Reversibility, Recency).
    /// </summary>
    public IReadOnlyList<ConfidenceComponent> Components { get; }

    /// <summary>
    /// Optional human-readable note explaining why the overall score
    /// is above or below the threshold.
    /// </summary>
    public string? Note { get; }

    /// <summary>
    /// The user/system threshold in [0.0, 1.0] against which the overall score is compared.
    /// </summary>
    public double Threshold { get; }

    public ConfidenceBreakdown(
        double overall,
        IReadOnlyList<ConfidenceComponent> components,
        string? note,
        double threshold)
    {
        if (double.IsNaN(overall) || double.IsInfinity(overall))
            throw new DomainException(ErrorCodes.ValidationError,
                "Overall confidence must be a finite number.");

        if (overall < 0.0 || overall > 1.0)
            throw new DomainException(ErrorCodes.ValidationError,
                $"Overall confidence must be between 0.0 and 1.0, but was {overall}.");

        if (double.IsNaN(threshold) || double.IsInfinity(threshold))
            throw new DomainException(ErrorCodes.ValidationError,
                "Threshold must be a finite number.");

        if (threshold < 0.0 || threshold > 1.0)
            throw new DomainException(ErrorCodes.ValidationError,
                $"Threshold must be between 0.0 and 1.0, but was {threshold}.");

        if (components is null)
            throw new DomainException(ErrorCodes.ValidationError,
                "Components list cannot be null.");

        Overall = overall;
        Components = components.ToArray();
        Note = note;
        Threshold = threshold;
    }

    /// <summary>
    /// True when the overall confidence meets or exceeds the threshold.
    /// </summary>
    public bool MeetsThreshold => Overall >= Threshold;

    public bool Equals(ConfidenceBreakdown? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (!Overall.Equals(other.Overall)
            || !Threshold.Equals(other.Threshold)
            || Note != other.Note
            || Components.Count != other.Components.Count)
            return false;

        for (int i = 0; i < Components.Count; i++)
        {
            if (!Components[i].Equals(other.Components[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ConfidenceBreakdown);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Overall);
        hash.Add(Threshold);
        hash.Add(Note);
        foreach (var c in Components)
            hash.Add(c);
        return hash.ToHashCode();
    }

    public static bool operator ==(ConfidenceBreakdown? left, ConfidenceBreakdown? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(ConfidenceBreakdown? left, ConfidenceBreakdown? right) => !(left == right);

    public override string ToString() =>
        $"Overall={Overall:F3}, Threshold={Threshold:F3}, MeetsThreshold={MeetsThreshold}, Components={Components.Count}";
}
