using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Confidence;

/// <summary>
/// Immutable value object representing a single named component of a confidence breakdown.
/// For example: "Pattern match", "Reach", "Reversibility", "Recency".
/// </summary>
public sealed class ConfidenceComponent : IEquatable<ConfidenceComponent>
{
    /// <summary>
    /// The component name (e.g. "Pattern match", "Reach").
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The component score in [0.0, 1.0].
    /// </summary>
    public double Value { get; }

    public ConfidenceComponent(string key, double value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new DomainException(ErrorCodes.ValidationError,
                "Confidence component key cannot be empty.");

        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new DomainException(ErrorCodes.ValidationError,
                "Confidence component value must be a finite number.");

        if (value < 0.0 || value > 1.0)
            throw new DomainException(ErrorCodes.ValidationError,
                $"Confidence component value must be between 0.0 and 1.0, but was {value}.");

        Key = key;
        Value = value;
    }

    public bool Equals(ConfidenceComponent? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Key == other.Key && Value.Equals(other.Value);
    }

    public override bool Equals(object? obj) => Equals(obj as ConfidenceComponent);

    public override int GetHashCode() => HashCode.Combine(Key, Value);

    public static bool operator ==(ConfidenceComponent? left, ConfidenceComponent? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(ConfidenceComponent? left, ConfidenceComponent? right) => !(left == right);

    public override string ToString() => $"{Key}: {Value:F3}";
}
