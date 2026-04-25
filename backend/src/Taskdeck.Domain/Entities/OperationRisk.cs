namespace Taskdeck.Domain.Entities;

/// <summary>
/// Value object representing a risk assessment for a proposal operation.
/// Immutable after creation.
/// </summary>
public sealed class OperationRisk : IEquatable<OperationRisk>
{
    public RiskLevel Level { get; }
    public string Reason { get; }

    public OperationRisk(RiskLevel level, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Risk reason cannot be empty.", nameof(reason));

        Level = level;
        Reason = reason;
    }

    public bool Equals(OperationRisk? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Level == other.Level && Reason == other.Reason;
    }

    public override bool Equals(object? obj) => Equals(obj as OperationRisk);

    public override int GetHashCode() => HashCode.Combine(Level, Reason);

    public override string ToString() => $"{Level}: {Reason}";
}
