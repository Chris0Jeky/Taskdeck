namespace Taskdeck.Application.DTOs;

/// <summary>
/// Response DTO for a single day in the streak grid.
/// </summary>
public sealed record StreakDayResponse(
    DateOnly Date,
    bool IsSealed,
    int IntensityBucket);

/// <summary>
/// Response DTO for the full streak query result.
/// </summary>
public sealed record StreakResponse(
    IReadOnlyList<StreakDayResponse> Days,
    int CurrentStreakLength,
    int LongestStreakLength,
    int DayCount);
