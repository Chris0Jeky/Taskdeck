namespace Taskdeck.Domain.Entities;

/// <summary>
/// Value object representing a single conflict/warning/status row for a proposal.
/// Immutable after creation.
/// </summary>
public sealed class ConflictRow : IEquatable<ConflictRow>
{
    public ConflictTone Tone { get; }
    public string Key { get; }
    public string Value { get; }

    public ConflictRow(ConflictTone tone, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Conflict row key cannot be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Conflict row value cannot be empty.", nameof(value));
        if (!Enum.IsDefined(typeof(ConflictTone), tone))
            throw new ArgumentException($"Invalid conflict tone: {tone}", nameof(tone));

        Tone = tone;
        Key = key;
        Value = value;
    }

    public bool Equals(ConflictRow? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Tone == other.Tone && Key == other.Key && Value == other.Value;
    }

    public override bool Equals(object? obj) => Equals(obj as ConflictRow);

    public override int GetHashCode() => HashCode.Combine(Tone, Key, Value);

    public override string ToString() => $"[{Tone}] {Key}: {Value}";
}
