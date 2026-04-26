namespace Taskdeck.Domain.Entities;

/// <summary>
/// Value object representing a single day in a streak grid.
/// IntensityBucket ranges from 0 (no activity) to 4 (highest quartile).
/// </summary>
public sealed record StreakDay
{
    public DateOnly Date { get; }
    public bool IsSealed { get; }
    public int IntensityBucket { get; }

    public StreakDay(DateOnly date, bool isSealed, int intensityBucket)
    {
        if (intensityBucket < 0 || intensityBucket > 4)
            throw new ArgumentOutOfRangeException(
                nameof(intensityBucket),
                intensityBucket,
                "IntensityBucket must be between 0 and 4.");

        Date = date;
        IsSealed = isSealed;
        IntensityBucket = intensityBucket;
    }
}
