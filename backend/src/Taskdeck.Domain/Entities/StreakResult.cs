namespace Taskdeck.Domain.Entities;

/// <summary>
/// Value object representing a streak query result with daily activity data
/// and computed streak metrics.
/// </summary>
public sealed record StreakResult
{
    public IReadOnlyList<StreakDay> Days { get; }
    public int CurrentStreakLength { get; }
    public int LongestStreakLength { get; }

    public StreakResult(
        IReadOnlyList<StreakDay> days,
        int currentStreakLength,
        int longestStreakLength)
    {
        ArgumentNullException.ThrowIfNull(days);

        if (currentStreakLength < 0)
            throw new ArgumentOutOfRangeException(
                nameof(currentStreakLength),
                currentStreakLength,
                "CurrentStreakLength cannot be negative.");

        if (longestStreakLength < 0)
            throw new ArgumentOutOfRangeException(
                nameof(longestStreakLength),
                longestStreakLength,
                "LongestStreakLength cannot be negative.");

        if (currentStreakLength > longestStreakLength)
            throw new ArgumentException(
                "CurrentStreakLength cannot exceed LongestStreakLength.",
                nameof(currentStreakLength));

        Days = days;
        CurrentStreakLength = currentStreakLength;
        LongestStreakLength = longestStreakLength;
    }
}
