namespace Taskdeck.Domain.Entities;

/// <summary>
/// Value object representing a single side-effect category and its active/passive classification
/// for a proposal review.
/// </summary>
public sealed class SideEffectRow : IEquatable<SideEffectRow>
{
    /// <summary>Category key (e.g. "Cards", "Subtasks", "Comments").</summary>
    public string Key { get; }

    /// <summary>Human-readable description of what happens in this category.</summary>
    public string Value { get; }

    /// <summary>Whether the proposal actively or passively affects this category.</summary>
    public SideEffectTone Tone { get; }

    public SideEffectRow(string key, string value, SideEffectTone tone)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Side-effect key cannot be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Side-effect value cannot be empty.", nameof(value));
        if (!Enum.IsDefined(tone))
            throw new ArgumentOutOfRangeException(nameof(tone), tone, "Unrecognized SideEffectTone value.");

        Key = key;
        Value = value;
        Tone = tone;
    }

    public bool Equals(SideEffectRow? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Key == other.Key && Value == other.Value && Tone == other.Tone;
    }

    public override bool Equals(object? obj) => Equals(obj as SideEffectRow);

    public override int GetHashCode() => HashCode.Combine(Key, Value, Tone);

    public override string ToString() => $"[{Tone}] {Key}: {Value}";
}
