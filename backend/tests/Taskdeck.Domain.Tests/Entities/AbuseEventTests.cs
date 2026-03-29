using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class AbuseEventTests
{
    private readonly Guid _actorUserId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateEvent_WithValidData()
    {
        var before = DateTimeOffset.UtcNow;

        var abuseEvent = new AbuseEvent(
            _actorUserId,
            AbuseSignalType.AnomalousVelocity,
            AbuseState.Observe,
            AbuseState.Suspicious,
            AbuseContainmentAction.StricterThrottles,
            "High request velocity detected");

        abuseEvent.ActorUserId.Should().Be(_actorUserId);
        abuseEvent.SignalType.Should().Be(AbuseSignalType.AnomalousVelocity);
        abuseEvent.PreviousState.Should().Be(AbuseState.Observe);
        abuseEvent.NewState.Should().Be(AbuseState.Suspicious);
        abuseEvent.ContainmentAction.Should().Be(AbuseContainmentAction.StricterThrottles);
        abuseEvent.Reason.Should().Be("High request velocity detected");
        abuseEvent.OperatorUserId.Should().BeNull();
        abuseEvent.Id.Should().NotBe(Guid.Empty);
        abuseEvent.CreatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Constructor_ShouldAcceptOperatorUserId()
    {
        var operatorId = Guid.NewGuid();

        var abuseEvent = new AbuseEvent(
            _actorUserId,
            AbuseSignalType.ManualOverride,
            AbuseState.Blocked,
            AbuseState.Observe,
            AbuseContainmentAction.None,
            "False positive confirmed",
            operatorId);

        abuseEvent.OperatorUserId.Should().Be(operatorId);
        abuseEvent.SignalType.Should().Be(AbuseSignalType.ManualOverride);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenActorUserIdEmpty()
    {
        var act = () => new AbuseEvent(
            Guid.Empty,
            AbuseSignalType.AnomalousVelocity,
            AbuseState.Observe,
            AbuseState.Suspicious,
            AbuseContainmentAction.StricterThrottles,
            "Test reason");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenReasonEmpty()
    {
        var act = () => new AbuseEvent(
            _actorUserId,
            AbuseSignalType.AnomalousVelocity,
            AbuseState.Observe,
            AbuseState.Suspicious,
            AbuseContainmentAction.StricterThrottles,
            "");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenReasonWhitespace()
    {
        var act = () => new AbuseEvent(
            _actorUserId,
            AbuseSignalType.AnomalousVelocity,
            AbuseState.Observe,
            AbuseState.Suspicious,
            AbuseContainmentAction.StricterThrottles,
            "   ");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(AbuseSignalType.AnomalousVelocity)]
    [InlineData(AbuseSignalType.RepeatedBlockedContent)]
    [InlineData(AbuseSignalType.LimitHitEvasion)]
    [InlineData(AbuseSignalType.SuspiciousConcentration)]
    [InlineData(AbuseSignalType.ManualEscalation)]
    [InlineData(AbuseSignalType.ManualOverride)]
    public void Constructor_ShouldAcceptAllSignalTypes(AbuseSignalType signalType)
    {
        var abuseEvent = new AbuseEvent(
            _actorUserId,
            signalType,
            AbuseState.Observe,
            AbuseState.Observe,
            AbuseContainmentAction.None,
            "Test signal");

        abuseEvent.SignalType.Should().Be(signalType);
    }
}
