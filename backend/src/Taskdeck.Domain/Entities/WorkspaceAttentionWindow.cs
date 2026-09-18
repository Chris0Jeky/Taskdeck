namespace Taskdeck.Domain.Entities;

/// <summary>A weekly window in a named time zone; overnight windows belong to their starting day.</summary>
public sealed record WorkspaceAttentionWindow(string TimeZoneId, int DaysMask, int StartMinute, int EndMinute)
{
    public bool IsValid() => ResolveZone() != null && DaysMask is > 0 and <= 127
        && StartMinute is >= 0 and < 1440 && EndMinute is >= 0 and < 1440 && StartMinute != EndMinute;

    public bool Contains(DateTimeOffset now)
    {
        if (!IsValid() || ResolveZone() is not { } zone) return false;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var minute = local.Hour * 60 + local.Minute;
        var day = (int)local.DayOfWeek;
        if (StartMinute < EndMinute)
            return HasDay(day) && minute >= StartMinute && minute < EndMinute;
        return minute >= StartMinute ? HasDay(day) : minute < EndMinute && HasDay((day + 6) % 7);
    }

    private bool HasDay(int day) => (DaysMask & (1 << day)) != 0;
    private TimeZoneInfo? ResolveZone()
    {
        // Store portable IANA names or UTC, never a platform-specific Windows display identifier.
        if (string.IsNullOrWhiteSpace(TimeZoneId) || TimeZoneId.Length > 100
            || (TimeZoneId != "UTC" && !TimeZoneId.Contains('/'))) return null;
        return TimeZoneInfo.TryFindSystemTimeZoneById(TimeZoneId, out var zone) ? zone : null;
    }
}
