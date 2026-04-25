using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Computes streak data by querying audit log entries for a user,
/// grouping by date, computing intensity quartiles, and calculating
/// current/longest streak lengths.
/// </summary>
public class StreakService : IStreakService
{
    private readonly IUnitOfWork _unitOfWork;

    public StreakService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StreakResult>> GetStreakAsync(
        Guid userId,
        int dayCount = 90,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<StreakResult>(ErrorCodes.ValidationError, "User ID cannot be empty.");

        if (dayCount < 1 || dayCount > 365)
            return Result.Failure<StreakResult>(ErrorCodes.ValidationError, "Day count must be between 1 and 365.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-(dayCount - 1));

        var from = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(today.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        // Use a lightweight projection that groups and counts on the server
        // instead of loading full AuditLog entities into memory.
        var dailyAuditCounts = await _unitOfWork.AuditLogs.CountByDateAsync(
            from, to, userId, cancellationToken);

        var dailyCounts = dailyAuditCounts.ToDictionary(d => d.Date, d => d.Count);

        // Compute intensity buckets via quartile-based bucketing
        var days = ComputeDays(startDate, today, dailyCounts);
        var currentStreak = ComputeCurrentStreak(days);
        var longestStreak = ComputeLongestStreak(days);

        return Result.Success(new StreakResult(days, currentStreak, longestStreak));
    }

    /// <summary>
    /// Build a StreakDay for each date in the range.
    /// Intensity bucket: 0 = no activity; 1-4 = quartile-based on max daily count.
    /// </summary>
    internal static IReadOnlyList<StreakDay> ComputeDays(
        DateOnly startDate,
        DateOnly endDate,
        Dictionary<DateOnly, int> dailyCounts)
    {
        var maxCount = dailyCounts.Count > 0 ? dailyCounts.Values.Max() : 0;
        var days = new List<StreakDay>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var count = dailyCounts.GetValueOrDefault(date, 0);
            var bucket = ComputeIntensityBucket(count, maxCount);
            // IsSealed defaults to false -- will be driven by #1017 seal entity
            days.Add(new StreakDay(date, isSealed: false, intensityBucket: bucket));
        }

        return days;
    }

    /// <summary>
    /// Maps a count to a 0-4 intensity bucket.
    /// 0 = no activity; 1-4 = quartile position within the max.
    /// When maxCount is 0, all days are bucket 0.
    /// </summary>
    internal static int ComputeIntensityBucket(int count, int maxCount)
    {
        if (count == 0 || maxCount == 0)
            return 0;

        // Compute position as a fraction of max, then map to 1-4
        var ratio = (double)count / maxCount;

        return ratio switch
        {
            <= 0.25 => 1,
            <= 0.50 => 2,
            <= 0.75 => 3,
            _ => 4
        };
    }

    /// <summary>
    /// Computes current streak: consecutive days with activity (bucket > 0)
    /// counting backwards from the last day (today).
    /// </summary>
    internal static int ComputeCurrentStreak(IReadOnlyList<StreakDay> days)
    {
        if (days.Count == 0)
            return 0;

        var streak = 0;
        for (var i = days.Count - 1; i >= 0; i--)
        {
            if (days[i].IntensityBucket > 0)
                streak++;
            else
                break;
        }

        return streak;
    }

    /// <summary>
    /// Computes the longest streak of consecutive days with activity (bucket > 0).
    /// </summary>
    internal static int ComputeLongestStreak(IReadOnlyList<StreakDay> days)
    {
        var longest = 0;
        var current = 0;

        foreach (var day in days)
        {
            if (day.IntensityBucket > 0)
            {
                current++;
                if (current > longest)
                    longest = current;
            }
            else
            {
                current = 0;
            }
        }

        return longest;
    }
}
