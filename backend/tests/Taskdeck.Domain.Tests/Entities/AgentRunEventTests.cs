using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class AgentRunEventTests
{
    private readonly Guid _runId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateEvent_WithValidParameters()
    {
        var evt = new AgentRunEvent(_runId, 0, "status_change", "{\"from\":\"Queued\",\"to\":\"GatheringContext\"}");

        evt.RunId.Should().Be(_runId);
        evt.SequenceNumber.Should().Be(0);
        evt.EventType.Should().Be("status_change");
        evt.Payload.Should().Be("{\"from\":\"Queued\",\"to\":\"GatheringContext\"}");
        evt.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        evt.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_ShouldDefaultPayload_WhenNull()
    {
        var evt = new AgentRunEvent(_runId, 1, "step_started");

        evt.Payload.Should().Be("{}");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRunIdIsEmpty()
    {
        var act = () => new AgentRunEvent(Guid.Empty, 0, "status_change");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*RunId*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSequenceNumberIsNegative()
    {
        var act = () => new AgentRunEvent(_runId, -1, "status_change");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*SequenceNumber*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEventTypeIsEmpty()
    {
        var act = () => new AgentRunEvent(_runId, 0, "");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*EventType*");
    }

    [Fact]
    public void Constructor_ShouldAcceptZeroSequenceNumber()
    {
        var evt = new AgentRunEvent(_runId, 0, "init");

        evt.SequenceNumber.Should().Be(0);
    }
}
