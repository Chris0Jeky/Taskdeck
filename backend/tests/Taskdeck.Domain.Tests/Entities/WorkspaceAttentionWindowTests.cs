using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class WorkspaceAttentionWindowTests
{
    [Theory]
    [InlineData("2026-09-07T08:59:00Z", false)]
    [InlineData("2026-09-07T09:00:00Z", true)]
    [InlineData("2026-09-07T16:59:59Z", true)]
    [InlineData("2026-09-07T17:00:00Z", false)]
    [InlineData("2026-09-08T10:00:00Z", false)]
    public void WindowIncludesStartExcludesEndAndUnselectedDays(string time, bool allowed) =>
        new WorkspaceAttentionWindow("UTC", 2, 540, 1020).Contains(DateTimeOffset.Parse(time)).Should().Be(allowed);

    [Theory]
    [InlineData("2026-09-07T21:59:00Z", false)]
    [InlineData("2026-09-07T22:00:00Z", true)]
    [InlineData("2026-09-08T01:59:00Z", true)]
    [InlineData("2026-09-08T02:00:00Z", false)]
    [InlineData("2026-09-08T22:00:00Z", false)]
    public void OvernightUsesTheSelectedStartingDay(string time, bool allowed) =>
        new WorkspaceAttentionWindow("UTC", 2, 1320, 120).Contains(DateTimeOffset.Parse(time)).Should().Be(allowed);

    [Theory]
    [InlineData("2026-01-05T13:00:00Z", false)]
    [InlineData("2026-01-05T14:00:00Z", true)]
    [InlineData("2026-07-06T13:00:00Z", true)]
    [InlineData("2026-11-01T05:30:00Z", true)]
    [InlineData("2026-11-01T06:30:00Z", true)]
    public void NamedZoneFollowsSeasonalAndRepeatedLocalHours(string time, bool allowed)
    {
        var now = DateTimeOffset.Parse(time);
        var window = now.Month == 11 ? new WorkspaceAttentionWindow("America/New_York", 1, 60, 120)
            : new WorkspaceAttentionWindow("America/New_York", 2, 540, 1020);
        window.Contains(now).Should().Be(allowed);
    }

    [Theory]
    [InlineData("Unknown/Place", 2, 540, 1020)]
    [InlineData("Eastern Standard Time", 2, 540, 1020)]
    [InlineData("UTC", 0, 540, 1020)]
    [InlineData("UTC", 128, 540, 1020)]
    [InlineData("UTC", 2, -1, 1020)]
    [InlineData("UTC", 2, 540, 1440)]
    [InlineData("UTC", 2, 540, 540)]
    public void InvalidWindowsFailClosed(string zone, int days, int start, int end)
    {
        var window = new WorkspaceAttentionWindow(zone, days, start, end);
        window.IsValid().Should().BeFalse();
        new WorkspaceAttention(Enabled: true, Window: window).CanClaim(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void WindowDoesNotResetUtcBudgetOrSpacing()
    {
        var now = DateTimeOffset.Parse("2026-09-07T10:00:00Z");
        var state = new WorkspaceAttention(Enabled: true).Claim(Guid.NewGuid(), now)!;
        var scheduled = state with { Window = new("UTC", 2, 540, 1020) };
        scheduled.CanClaim(now.AddMinutes(1)).Should().BeFalse();
        scheduled.CanClaim(now.AddHours(2)).Should().BeTrue();
        scheduled.Claim(Guid.NewGuid(), now.AddHours(2))!.CanClaim(now.AddHours(4)).Should().BeFalse();
    }
}
