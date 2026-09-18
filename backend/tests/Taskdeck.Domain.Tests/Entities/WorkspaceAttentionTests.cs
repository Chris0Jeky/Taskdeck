using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class WorkspaceAttentionTests
{
    [Fact]
    public void DefaultIsOffAndTogglingDoesNotResetTheSharedBudget()
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        new WorkspaceAttention().Claim(first, now).Should().BeNull();
        var state = new WorkspaceAttention(Enabled: true).Claim(first, now)!;
        state.Claim(second, now.AddMinutes(119)).Should().BeNull();
        state.Claim(first, now.AddHours(2)).Should().BeNull();
        state = state.Claim(second, now.AddHours(2))!;
        state.Claim(Guid.NewGuid(), now.AddHours(4)).Should().BeNull();
        var toggled = (state with { Enabled = false }) with { Enabled = true };
        toggled.Claim(Guid.NewGuid(), now.AddHours(5)).Should().BeNull();
        toggled.Claim(Guid.NewGuid(), now.AddDays(1))!.Count.Should().Be(1);
    }
    [Fact]
    public void MidnightDoesNotBypassSpacingAndClockRollbackDoesNotCreateCapacity()
    {
        var now = new DateTimeOffset(2026, 9, 10, 23, 30, 0, TimeSpan.Zero);
        var state = new WorkspaceAttention(Enabled: true).Claim(Guid.NewGuid(), now)!;
        state.Claim(Guid.NewGuid(), now.AddMinutes(31)).Should().BeNull();
        state.Claim(Guid.NewGuid(), now.AddDays(-1)).Should().BeNull();
        state.Claim(Guid.NewGuid(), now.AddHours(2))!.Count.Should().Be(1);
    }
}
