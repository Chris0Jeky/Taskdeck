namespace Taskdeck.Domain.Common;

/// <summary>
/// A single hour bucket in a daily cadence: how many events occurred in that hour.
/// </summary>
public sealed record CadenceBucket
{
    public int Hour { get; }
    public int EventCount { get; }

    public CadenceBucket(int hour, int eventCount)
    {
        if (hour < 0 || hour > 23)
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "Hour must be between 0 and 23.");

        if (eventCount < 0)
            throw new ArgumentOutOfRangeException(nameof(eventCount), eventCount, "EventCount must be non-negative.");

        Hour = hour;
        EventCount = eventCount;
    }
}

/// <summary>
/// Aggregated per-hour activity snapshot for a single day, used to drive the
/// Today Cadence strip (24 SVG bars + first/peak/last action).
/// </summary>
public sealed record CadenceSnapshot
{
    private const int RequiredBucketCount = 24;

    private static readonly CadenceSnapshot EmptyInstance = new(
        Enumerable.Range(0, RequiredBucketCount)
            .Select(h => new CadenceBucket(h, 0))
            .ToList()
            .AsReadOnly(),
        null, null, null);

    public IReadOnlyList<CadenceBucket> Buckets { get; }
    public DateTimeOffset? FirstActionAt { get; }
    public int? PeakHour { get; }
    public DateTimeOffset? LastActionAt { get; }

    public CadenceSnapshot(
        IReadOnlyList<CadenceBucket> buckets,
        DateTimeOffset? firstActionAt,
        int? peakHour,
        DateTimeOffset? lastActionAt)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        if (buckets.Count != RequiredBucketCount)
            throw new ArgumentException(
                $"Buckets must contain exactly {RequiredBucketCount} entries, got {buckets.Count}.",
                nameof(buckets));

        Buckets = buckets;
        FirstActionAt = firstActionAt;
        PeakHour = peakHour;
        LastActionAt = lastActionAt;
    }

    /// <summary>
    /// Returns a cached empty snapshot representing a day with no activity.
    /// All 24 buckets are present with zero counts.
    /// </summary>
    public static CadenceSnapshot Empty() => EmptyInstance;
}
