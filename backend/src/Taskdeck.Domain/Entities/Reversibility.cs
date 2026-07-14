namespace Taskdeck.Domain.Entities;

/// <summary>
/// Compatibility value object behind the side-effect endpoint's historical reversibility field.
/// Its copy describes apply risk and possible manual recovery. WindowMs is legacy
/// review-attention metadata and does not represent an undo capability.
/// </summary>
public sealed class Reversibility : IEquatable<Reversibility>
{
    /// <summary>Legacy default review-attention horizon: 6 hours in milliseconds.</summary>
    public const long DefaultWindowMs = 6L * 60 * 60 * 1000; // 21_600_000

    /// <summary>Short apply-risk summary.</summary>
    public string Summary { get; }

    /// <summary>Detailed description of the apply risk and possible manual recovery.</summary>
    public string Description { get; }

    /// <summary>Legacy review-attention metadata retained for contract compatibility.</summary>
    public long WindowMs { get; }

    public Reversibility(string summary, string description, long windowMs)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("Apply-risk summary cannot be empty.", nameof(summary));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Apply-risk description cannot be empty.", nameof(description));
        if (windowMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowMs), "Review-attention metadata must be positive.");

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
