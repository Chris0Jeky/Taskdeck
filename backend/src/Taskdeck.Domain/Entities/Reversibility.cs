namespace Taskdeck.Domain.Entities;

/// <summary>
/// Value object representing the reversibility window for a proposal.
/// Describes how long a user has to undo the effects and the effort required.
/// </summary>
public sealed class Reversibility : IEquatable<Reversibility>
{
    /// <summary>Default reversibility window: 6 hours in milliseconds.</summary>
    public const long DefaultWindowMs = 6L * 60 * 60 * 1000; // 21_600_000

    /// <summary>Short summary (e.g. "6 hours - single keystroke").</summary>
    public string Summary { get; }

    /// <summary>Detailed description of the reversibility posture.</summary>
    public string Description { get; }

    /// <summary>Reversibility window in milliseconds.</summary>
    public long WindowMs { get; }

    public Reversibility(string summary, string description, long windowMs)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("Reversibility summary cannot be empty.", nameof(summary));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Reversibility description cannot be empty.", nameof(description));
        if (windowMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowMs), "Reversibility window must be positive.");

        Summary = summary;
        Description = description;
        WindowMs = windowMs;
    }

    public bool Equals(Reversibility? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Summary == other.Summary && Description == other.Description && WindowMs == other.WindowMs;
    }

    public override bool Equals(object? obj) => Equals(obj as Reversibility);

    public override int GetHashCode() => HashCode.Combine(Summary, Description, WindowMs);

    public override string ToString() => $"{Summary} ({WindowMs}ms)";
}
