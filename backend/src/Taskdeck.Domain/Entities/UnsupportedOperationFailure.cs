namespace Taskdeck.Domain.Entities;

/// <summary>
/// Structured failure for an operation that the compiler cannot process.
/// Immutable after creation.
/// </summary>
public sealed class UnsupportedOperationFailure : IEquatable<UnsupportedOperationFailure>
{
    /// <summary>The action type that is unsupported.</summary>
    public string ActionType { get; }

    /// <summary>The target type the operation references.</summary>
    public string TargetType { get; }

    /// <summary>Human-readable reason why this operation is unsupported.</summary>
    public string Reason { get; }

    public UnsupportedOperationFailure(string actionType, string targetType, string reason)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            throw new ArgumentException("ActionType cannot be empty.", nameof(actionType));
        if (string.IsNullOrWhiteSpace(targetType))
            throw new ArgumentException("TargetType cannot be empty.", nameof(targetType));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be empty.", nameof(reason));

        ActionType = actionType;
        TargetType = targetType;
        Reason = reason;
    }

    public bool Equals(UnsupportedOperationFailure? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ActionType == other.ActionType
            && TargetType == other.TargetType
            && Reason == other.Reason;
    }

    public override bool Equals(object? obj) => Equals(obj as UnsupportedOperationFailure);

    public override int GetHashCode() => HashCode.Combine(ActionType, TargetType, Reason);

    public override string ToString() => $"Unsupported: {ActionType} on {TargetType} - {Reason}";
}
