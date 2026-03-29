using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AbuseDetectionServiceTests
{
    private readonly AbuseDetectionSettings _settings;
    private readonly AbuseDetectionState _state;
    private readonly Mock<ILlmUsageRecordRepository> _usageRecordsMock;

    public AbuseDetectionServiceTests()
    {
        _settings = new AbuseDetectionSettings
        {
            Enabled = true,
            SuspiciousSignalThreshold = 3,
            RestrictedSignalThreshold = 6,
            BlockedSignalThreshold = 10,
            VelocityRequestsPerHourThreshold = 120,
            VelocityTokensPerHourThreshold = 200_000
        };
        _state = new AbuseDetectionState();
        _usageRecordsMock = new Mock<ILlmUsageRecordRepository>();
    }

    private AbuseDetectionService CreateService() => new(_settings, _state, _usageRecordsMock.Object);

    [Fact]
    public async Task RecordSignal_ShouldReturnNoChange_WhenDisabled()
    {
        _settings.Enabled = false;
        var service = CreateService();
        var userId = Guid.NewGuid();

        var result = await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, "Test");

        result.StateChanged.Should().BeFalse();
        result.NewState.Should().Be(AbuseState.Observe);
        result.ContainmentAction.Should().Be(AbuseContainmentAction.None);
    }

    [Fact]
    public async Task RecordSignal_ShouldStayObserve_BelowThreshold()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        var result = await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, "Signal 1");

        result.StateChanged.Should().BeFalse();
        result.PreviousState.Should().Be(AbuseState.Observe);
        result.NewState.Should().Be(AbuseState.Observe);
    }

    [Fact]
    public async Task RecordSignal_ShouldEscalateToSuspicious_AtThreshold()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        for (int i = 0; i < _settings.SuspiciousSignalThreshold - 1; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");
        }

        var result = await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, "Threshold signal");

        result.StateChanged.Should().BeTrue();
        result.PreviousState.Should().Be(AbuseState.Observe);
        result.NewState.Should().Be(AbuseState.Suspicious);
        result.ContainmentAction.Should().Be(AbuseContainmentAction.StricterThrottles);
    }

    [Fact]
    public async Task RecordSignal_ShouldEscalateToRestricted_AtThreshold()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        for (int i = 0; i < _settings.RestrictedSignalThreshold - 1; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.LimitHitEvasion, $"Signal {i + 1}");
        }

        var result = await service.RecordSignalAsync(userId, AbuseSignalType.LimitHitEvasion, "Threshold signal");

        result.StateChanged.Should().BeTrue();
        result.NewState.Should().Be(AbuseState.Restricted);
        result.ContainmentAction.Should().Be(AbuseContainmentAction.ProviderCallsDisabled);
    }

    [Fact]
    public async Task RecordSignal_ShouldEscalateToBlocked_AtThreshold()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        for (int i = 0; i < _settings.BlockedSignalThreshold - 1; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.RepeatedBlockedContent, $"Signal {i + 1}");
        }

        var result = await service.RecordSignalAsync(userId, AbuseSignalType.RepeatedBlockedContent, "Threshold signal");

        result.StateChanged.Should().BeTrue();
        result.NewState.Should().Be(AbuseState.Blocked);
        result.ContainmentAction.Should().Be(AbuseContainmentAction.MandatoryManualReview);
    }

    [Fact]
    public async Task IsBlocked_ShouldReturnFalse_ForUnknownActor()
    {
        var service = CreateService();

        var blocked = await service.IsBlockedAsync(Guid.NewGuid());

        blocked.Should().BeFalse();
    }

    [Fact]
    public async Task IsBlocked_ShouldReturnFalse_WhenDisabled()
    {
        _settings.Enabled = false;
        var service = CreateService();

        var blocked = await service.IsBlockedAsync(Guid.NewGuid());

        blocked.Should().BeFalse();
    }

    [Fact]
    public async Task IsBlocked_ShouldReturnTrue_WhenRestricted()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        for (int i = 0; i < _settings.RestrictedSignalThreshold; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");
        }

        var blocked = await service.IsBlockedAsync(userId);

        blocked.Should().BeTrue();
    }

    [Fact]
    public async Task GetActorStatus_ShouldReturnDefaultForUnknownActor()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        var status = await service.GetActorStatusAsync(userId);

        status.UserId.Should().Be(userId);
        status.CurrentState.Should().Be(AbuseState.Observe);
        status.SignalCount.Should().Be(0);
        status.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task GetActorStatus_ShouldReflectCurrentState()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        for (int i = 0; i < _settings.SuspiciousSignalThreshold; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");
        }

        var status = await service.GetActorStatusAsync(userId);

        status.CurrentState.Should().Be(AbuseState.Suspicious);
        status.SignalCount.Should().Be(_settings.SuspiciousSignalThreshold);
        status.RequiresStricterThrottles.Should().BeTrue();
        status.EscalatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task OverrideActorState_ShouldDeescalateToObserve()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        for (int i = 0; i < _settings.BlockedSignalThreshold; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");
        }

        var result = await service.OverrideActorStateAsync(
            userId, AbuseState.Observe, "False positive confirmed", operatorId);

        result.IsSuccess.Should().BeTrue();

        var status = await service.GetActorStatusAsync(userId);
        status.CurrentState.Should().Be(AbuseState.Observe);
        status.IsBlocked.Should().BeFalse();
        status.SignalCount.Should().Be(0);
        status.LastOverrideAt.Should().NotBeNull();
        status.LastOverrideByUserId.Should().Be(operatorId);
    }

    [Fact]
    public async Task OverrideActorState_ShouldEscalateManually()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var result = await service.OverrideActorStateAsync(
            userId, AbuseState.Blocked, "Suspicious pattern identified manually", operatorId);

        result.IsSuccess.Should().BeTrue();

        var status = await service.GetActorStatusAsync(userId);
        status.CurrentState.Should().Be(AbuseState.Blocked);
        status.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task OverrideActorState_ShouldFail_WhenReasonEmpty()
    {
        var service = CreateService();

        var result = await service.OverrideActorStateAsync(
            Guid.NewGuid(), AbuseState.Observe, "", Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task OverrideActorState_ShouldFail_WhenOperatorIdEmpty()
    {
        var service = CreateService();

        var result = await service.OverrideActorStateAsync(
            Guid.NewGuid(), AbuseState.Observe, "Test", Guid.Empty);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetAuditTrail_ShouldReturnEventsInReverseChronologicalOrder()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, "Signal 1");
        await service.RecordSignalAsync(userId, AbuseSignalType.RepeatedBlockedContent, "Signal 2");
        await service.RecordSignalAsync(userId, AbuseSignalType.LimitHitEvasion, "Signal 3");

        var events = await service.GetAuditTrailAsync(userId);

        events.Should().HaveCount(3);
        events[0].Reason.Should().Be("Signal 3");
        events[1].Reason.Should().Be("Signal 2");
        events[2].Reason.Should().Be("Signal 1");
    }

    [Fact]
    public async Task GetAuditTrail_ShouldIncludeOverrideEvents()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        // Escalate to Suspicious first so override back to Observe is a de-escalation
        for (int i = 0; i < _settings.SuspiciousSignalThreshold; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");
        }

        await service.OverrideActorStateAsync(userId, AbuseState.Observe, "False positive", operatorId);

        var events = await service.GetAuditTrailAsync(userId);

        events.Should().HaveCount(_settings.SuspiciousSignalThreshold + 1);
        events[0].SignalType.Should().Be(AbuseSignalType.ManualOverride);
        events[0].OperatorUserId.Should().Be(operatorId);
        events[0].Reason.Should().Be("False positive");
    }

    [Fact]
    public async Task GetAuditTrail_ShouldRespectLimit()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");
        }

        var events = await service.GetAuditTrailAsync(userId, limit: 2);

        events.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAuditTrail_ShouldFilterByActor()
    {
        var service = CreateService();
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        await service.RecordSignalAsync(userId1, AbuseSignalType.AnomalousVelocity, "User1 signal");
        await service.RecordSignalAsync(userId2, AbuseSignalType.LimitHitEvasion, "User2 signal");

        var events1 = await service.GetAuditTrailAsync(userId1);
        var events2 = await service.GetAuditTrailAsync(userId2);

        events1.Should().HaveCount(1);
        events1[0].ActorUserId.Should().Be(userId1);
        events2.Should().HaveCount(1);
        events2[0].ActorUserId.Should().Be(userId2);
    }

    [Fact]
    public async Task EvaluateActor_ShouldReturnFalse_WhenDisabled()
    {
        _settings.Enabled = false;
        var service = CreateService();

        var result = await service.EvaluateActorAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateActor_ShouldDetectRequestVelocityAnomaly()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        _usageRecordsMock.Setup(r => r.GetRequestCountAsync(
                userId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(150);

        _usageRecordsMock.Setup(r => r.GetTotalTokensAsync(
                userId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var signalsDetected = await service.EvaluateActorAsync(userId);

        signalsDetected.Should().BeTrue();

        var status = await service.GetActorStatusAsync(userId);
        status.SignalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EvaluateActor_ShouldDetectTokenVelocityAnomaly()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        _usageRecordsMock.Setup(r => r.GetRequestCountAsync(
                userId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _usageRecordsMock.Setup(r => r.GetTotalTokensAsync(
                userId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(250_000);

        var signalsDetected = await service.EvaluateActorAsync(userId);

        signalsDetected.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateActor_ShouldReturnFalse_WhenBelowThresholds()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        _usageRecordsMock.Setup(r => r.GetRequestCountAsync(
                userId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        _usageRecordsMock.Setup(r => r.GetTotalTokensAsync(
                userId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100_000);

        var signalsDetected = await service.EvaluateActorAsync(userId);

        signalsDetected.Should().BeFalse();
    }

    [Fact]
    public async Task FullEscalationAndRollback_EndToEnd()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        // Escalate through all states
        for (int i = 0; i < _settings.BlockedSignalThreshold; i++)
        {
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");
        }

        var status = await service.GetActorStatusAsync(userId);
        status.CurrentState.Should().Be(AbuseState.Blocked);
        status.IsBlocked.Should().BeTrue();

        // Operator rollback
        var rollback = await service.OverrideActorStateAsync(
            userId, AbuseState.Observe, "Cleared after investigation", operatorId);
        rollback.IsSuccess.Should().BeTrue();

        status = await service.GetActorStatusAsync(userId);
        status.CurrentState.Should().Be(AbuseState.Observe);
        status.IsBlocked.Should().BeFalse();
        status.SignalCount.Should().Be(0);

        // Verify audit trail captures full history
        var events = await service.GetAuditTrailAsync(userId);
        events.Should().HaveCount(_settings.BlockedSignalThreshold + 1);
        events[0].SignalType.Should().Be(AbuseSignalType.ManualOverride);
        events[0].OperatorUserId.Should().Be(operatorId);
    }

    [Fact]
    public async Task OverrideActorState_ManualEscalation_ShouldRecordManualEscalationSignal()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        await service.OverrideActorStateAsync(
            userId, AbuseState.Blocked, "Manual block for investigation", operatorId);

        var events = await service.GetAuditTrailAsync(userId);
        events.Should().HaveCount(1);
        events[0].SignalType.Should().Be(AbuseSignalType.ManualEscalation);
    }

    [Fact]
    public async Task EvaluateActor_ShouldReturnFalse_WhenNoRepository()
    {
        var service = new AbuseDetectionService(_settings, _state, usageRecords: null);
        var userId = Guid.NewGuid();

        var result = await service.EvaluateActorAsync(userId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RecordSignal_ShouldEmitAuditEvent_ForEverySignal()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, "First");
        await service.RecordSignalAsync(userId, AbuseSignalType.RepeatedBlockedContent, "Second");

        var events = await service.GetAuditTrailAsync(userId);
        events.Should().HaveCount(2);
        events.All(e => e.ActorUserId == userId).Should().BeTrue();
    }

    [Fact]
    public async Task ContainmentTransitions_ShouldBeDeterministic()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        // At threshold 3: Suspicious
        for (int i = 0; i < 3; i++)
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");

        var status = await service.GetActorStatusAsync(userId);
        status.CurrentState.Should().Be(AbuseState.Suspicious);
        status.ActiveContainment.Should().Be(AbuseContainmentAction.StricterThrottles);

        // At threshold 6: Restricted
        for (int i = 3; i < 6; i++)
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");

        status = await service.GetActorStatusAsync(userId);
        status.CurrentState.Should().Be(AbuseState.Restricted);
        status.ActiveContainment.Should().Be(AbuseContainmentAction.ProviderCallsDisabled);

        // At threshold 10: Blocked
        for (int i = 6; i < 10; i++)
            await service.RecordSignalAsync(userId, AbuseSignalType.AnomalousVelocity, $"Signal {i + 1}");

        status = await service.GetActorStatusAsync(userId);
        status.CurrentState.Should().Be(AbuseState.Blocked);
        status.ActiveContainment.Should().Be(AbuseContainmentAction.MandatoryManualReview);
    }
}
