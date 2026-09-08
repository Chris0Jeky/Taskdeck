using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class QuietInsightTests
{
    [Fact]
    public void Snooze_OnlyExpiresOnRevalidatedEvidence()
    {
        var now = DateTimeOffset.UtcNow;
        var insight = new QuietInsight(Guid.NewGuid(), Guid.NewGuid(), "blocked", "card", null, null);
        insight.Refresh("Question", "Reason", "Evidence", now);
        insight.Act("snooze", now);
        insight.Refresh("Question", "Reason", "Evidence", now.AddHours(23));
        insight.State.Should().Be("snoozed");
        insight.Refresh("Question", "Reason", "Evidence", now.AddDays(1));
        insight.State.Should().Be("available");
    }
    [Fact]
    public void Dismissal_SurvivesChangedEvidence()
    {
        var insight = new QuietInsight(Guid.NewGuid(), Guid.NewGuid(), "blocked", "card", null, null);
        insight.Act("dismiss", DateTimeOffset.UtcNow);
        insight.Refresh("New wording", "Reason", "New evidence", DateTimeOffset.UtcNow);
        insight.State.Should().Be("dismissed");
    }
    [Fact]
    public void Memory_InvalidStatusIsRejected()
    {
        var act = () => new WorkspaceMemory(Guid.NewGuid(), Guid.NewGuid(), "Title", "Text", "model-confirmed");
        act.Should().Throw<DomainException>();
    }
}
