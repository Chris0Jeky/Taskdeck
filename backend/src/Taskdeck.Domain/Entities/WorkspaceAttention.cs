namespace Taskdeck.Domain.Entities;

/// <summary>Optional reminders share one conservative account budget across boards and clients.</summary>
public sealed record WorkspaceAttention(bool Enabled = false, DateOnly? Day = null, int Count = 0,
    DateTimeOffset? LastClaimedAt = null, Guid? LastInsightId = null)
{
    public const int DailyLimit = 2;
    public static readonly TimeSpan MinimumSpacing = TimeSpan.FromHours(2);
    public bool CanClaim(DateTimeOffset now) => Enabled
        && (Day != DateOnly.FromDateTime(now.UtcDateTime) || Count < DailyLimit)
        && (LastClaimedAt is null || now - LastClaimedAt >= MinimumSpacing);

    public WorkspaceAttention? Claim(Guid insightId, DateTimeOffset now)
    {
        if (insightId == Guid.Empty || insightId == LastInsightId || !CanClaim(now)) return null;
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        return this with { Day = day, Count = Day == day ? Count + 1 : 1, LastClaimedAt = now, LastInsightId = insightId };
    }
}
