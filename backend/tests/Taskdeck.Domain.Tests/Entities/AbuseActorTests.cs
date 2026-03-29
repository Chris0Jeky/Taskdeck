using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class AbuseActorTests
{
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateWithObserveState()
    {
        var actor = new AbuseActor(_userId);

        actor.UserId.Should().Be(_userId);
        actor.CurrentState.Should().Be(AbuseState.Observe);
        actor.ActiveContainment.Should().Be(AbuseContainmentAction.None);
        actor.SignalCount.Should().Be(0);
        actor.EscalatedAt.Should().BeNull();
        actor.LastOverrideAt.Should().BeNull();
        actor.LastOverrideByUserId.Should().BeNull();
        actor.IsBlocked.Should().BeFalse();
        actor.RequiresStricterThrottles.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdEmpty()
    {
        var act = () => new AbuseActor(Guid.Empty);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void RecordSignalAndEscalate_ShouldIncrementSignalCount()
    {
        var actor = new AbuseActor(_userId);

        actor.RecordSignalAndEscalate(AbuseState.Observe, AbuseContainmentAction.None);

        actor.SignalCount.Should().Be(1);
    }

    [Fact]
    public void RecordSignalAndEscalate_ShouldEscalateToSuspicious()
    {
        var actor = new AbuseActor(_userId);

        var changed = actor.RecordSignalAndEscalate(AbuseState.Suspicious, AbuseContainmentAction.StricterThrottles);

        changed.Should().BeTrue();
        actor.CurrentState.Should().Be(AbuseState.Suspicious);
        actor.ActiveContainment.Should().Be(AbuseContainmentAction.StricterThrottles);
        actor.EscalatedAt.Should().NotBeNull();
        actor.RequiresStricterThrottles.Should().BeTrue();
        actor.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void RecordSignalAndEscalate_ShouldEscalateToRestricted()
    {
        var actor = new AbuseActor(_userId);
        actor.RecordSignalAndEscalate(AbuseState.Suspicious, AbuseContainmentAction.StricterThrottles);

        var changed = actor.RecordSignalAndEscalate(AbuseState.Restricted, AbuseContainmentAction.ProviderCallsDisabled);

        changed.Should().BeTrue();
        actor.CurrentState.Should().Be(AbuseState.Restricted);
        actor.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void RecordSignalAndEscalate_ShouldEscalateToBlocked()
    {
        var actor = new AbuseActor(_userId);
        actor.RecordSignalAndEscalate(AbuseState.Restricted, AbuseContainmentAction.ProviderCallsDisabled);

        var changed = actor.RecordSignalAndEscalate(AbuseState.Blocked, AbuseContainmentAction.MandatoryManualReview);

        changed.Should().BeTrue();
        actor.CurrentState.Should().Be(AbuseState.Blocked);
        actor.ActiveContainment.Should().Be(AbuseContainmentAction.MandatoryManualReview);
        actor.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void RecordSignalAndEscalate_ShouldNotDeescalate()
    {
        var actor = new AbuseActor(_userId);
        actor.RecordSignalAndEscalate(AbuseState.Restricted, AbuseContainmentAction.ProviderCallsDisabled);

        var changed = actor.RecordSignalAndEscalate(AbuseState.Suspicious, AbuseContainmentAction.StricterThrottles);

        changed.Should().BeFalse();
        actor.CurrentState.Should().Be(AbuseState.Restricted);
    }

    [Fact]
    public void RecordSignalAndEscalate_ShouldNotChangeOnSameState()
    {
        var actor = new AbuseActor(_userId);
        actor.RecordSignalAndEscalate(AbuseState.Suspicious, AbuseContainmentAction.StricterThrottles);

        var changed = actor.RecordSignalAndEscalate(AbuseState.Suspicious, AbuseContainmentAction.StricterThrottles);

        changed.Should().BeFalse();
        actor.SignalCount.Should().Be(2);
    }

    [Fact]
    public void OverrideState_ShouldAllowDeescalation()
    {
        var actor = new AbuseActor(_userId);
        var operatorId = Guid.NewGuid();
        actor.RecordSignalAndEscalate(AbuseState.Blocked, AbuseContainmentAction.MandatoryManualReview);

        actor.OverrideState(AbuseState.Observe, AbuseContainmentAction.None, operatorId);

        actor.CurrentState.Should().Be(AbuseState.Observe);
        actor.ActiveContainment.Should().Be(AbuseContainmentAction.None);
        actor.SignalCount.Should().Be(0);
        actor.EscalatedAt.Should().BeNull();
        actor.LastOverrideAt.Should().NotBeNull();
        actor.LastOverrideByUserId.Should().Be(operatorId);
        actor.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void OverrideState_ShouldAllowEscalation()
    {
        var actor = new AbuseActor(_userId);
        var operatorId = Guid.NewGuid();

        actor.OverrideState(AbuseState.Blocked, AbuseContainmentAction.MandatoryManualReview, operatorId);

        actor.CurrentState.Should().Be(AbuseState.Blocked);
        actor.IsBlocked.Should().BeTrue();
        actor.LastOverrideByUserId.Should().Be(operatorId);
    }

    [Fact]
    public void OverrideState_ShouldThrow_WhenOperatorIdEmpty()
    {
        var actor = new AbuseActor(_userId);

        var act = () => actor.OverrideState(AbuseState.Observe, AbuseContainmentAction.None, Guid.Empty);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void DefaultContainmentFor_ShouldReturnCorrectActions()
    {
        AbuseActor.DefaultContainmentFor(AbuseState.Observe).Should().Be(AbuseContainmentAction.None);
        AbuseActor.DefaultContainmentFor(AbuseState.Suspicious).Should().Be(AbuseContainmentAction.StricterThrottles);
        AbuseActor.DefaultContainmentFor(AbuseState.Restricted).Should().Be(AbuseContainmentAction.ProviderCallsDisabled);
        AbuseActor.DefaultContainmentFor(AbuseState.Blocked).Should().Be(AbuseContainmentAction.MandatoryManualReview);
    }

    [Fact]
    public void IsBlocked_ShouldBeTrueForRestrictedAndBlocked()
    {
        var restricted = new AbuseActor(Guid.NewGuid());
        restricted.RecordSignalAndEscalate(AbuseState.Restricted, AbuseContainmentAction.ProviderCallsDisabled);
        restricted.IsBlocked.Should().BeTrue();

        var blocked = new AbuseActor(Guid.NewGuid());
        blocked.RecordSignalAndEscalate(AbuseState.Blocked, AbuseContainmentAction.MandatoryManualReview);
        blocked.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void RequiresStricterThrottles_ShouldBeTrueForSuspiciousAndAbove()
    {
        var suspicious = new AbuseActor(Guid.NewGuid());
        suspicious.RecordSignalAndEscalate(AbuseState.Suspicious, AbuseContainmentAction.StricterThrottles);
        suspicious.RequiresStricterThrottles.Should().BeTrue();

        var observe = new AbuseActor(Guid.NewGuid());
        observe.RequiresStricterThrottles.Should().BeFalse();
    }
}
