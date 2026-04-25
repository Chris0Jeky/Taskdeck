namespace Taskdeck.Domain.Common;

/// <summary>
/// A single hour bucket in a daily cadence: how many events occurred in that hour.
/// </summary>
public sealed record CadenceBucket(int Hour, int EventCount)
{
    /// <summary>
    /// Validates that Hour is in 0-23 and EventCount is non-negative.
    /// </summary>
    public CadenceBucket
    {
        if (Hour < 0 || Hour > 23)
            throw new ArgumentOutOfRangeException(nameof(Hour), Hour, "Hour must be between 0 and 23.");

        if (EventCount < 0)
            throw new ArgumentOutOfRangeException(nameof(EventCount), EventCount, "EventCount must be non-negative.");
    }
}

/// <summary>
/// Aggregated per-hour activity snapshot for a single day, used to drive the
/// Today Cadence strip (24 SVG bars + first/peak/last action).
/// </summary>
public sealed record CadenceSnapshot(
    IReadOnlyList<CadenceBucket> Buckets,
    DateTimeOffset? FirstActionAt,
    int? PeakHour,
    DateTimeOffset? LastActionAt)
{
    /// <summary>
    /// Returns an empty snapshot representing a day with no activity.
    /// All 24 buckets are present with zero counts.
    /// </summary>
    public static CadenceSnapshot Empty()
    {
        var buckets = Enumerable.Range(0, 24)
            .Select(h => new CadenceBucket(h, 0))
            .ToList()
            .AsReadOnly();

        return new CadenceSnapshot(buckets, null, null, null);
    }
}
