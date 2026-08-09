using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CircuitBreakerStateTrackerTests
{
    private static readonly CircuitBreakerSettings Settings = new()
    {
        FailureThreshold = 1,
        BreakDurationSeconds = 60
    };

    [Fact]
    public void AbandonedHalfOpenProbe_RemainsOpenForFullConfiguredBreak()
    {
        var clock = new ManualTimeProvider();
        var tracker = new CircuitBreakerStateTracker(clock);
        tracker.TryEnterProviderRequest("provider", Settings, out var initial, out _).Should().BeTrue();
        tracker.RecordProviderFailure("provider", Settings, "failed", initial);
        clock.Advance(TimeSpan.FromSeconds(60));
        tracker.TryEnterProviderRequest("provider", Settings, out var probe, out _).Should().BeTrue();

        tracker.AbandonProviderRequest("provider", Settings, probe);

        tracker.TryEnterProviderRequest("provider", Settings, out _, out _).Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(59));
        tracker.TryEnterProviderRequest("provider", Settings, out _, out _).Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.TryEnterProviderRequest("provider", Settings, out var nextProbe, out _).Should().BeTrue();
        nextProbe.IsHalfOpenProbe.Should().BeTrue();
    }

    [Fact]
    public void StalePreOpenSuccess_DoesNotCloseNewOpenGeneration()
    {
        var tracker = new CircuitBreakerStateTracker(new ManualTimeProvider());
        tracker.TryEnterProviderRequest("provider", Settings, out var failing, out _).Should().BeTrue();
        tracker.TryEnterProviderRequest("provider", Settings, out var staleSuccess, out _).Should().BeTrue();

        tracker.RecordProviderFailure("provider", Settings, "failed", failing);
        tracker.RecordProviderSuccess("provider", staleSuccess);

        tracker.Get("provider")!.State.Should().Be(CircuitState.Open);
        tracker.TryEnterProviderRequest("provider", Settings, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void StalePreOpenFailure_DoesNotReopenAfterSuccessfulHalfOpenProbe()
    {
        var clock = new ManualTimeProvider();
        var tracker = new CircuitBreakerStateTracker(clock);
        tracker.TryEnterProviderRequest("provider", Settings, out var openingFailure, out _).Should().BeTrue();
        tracker.TryEnterProviderRequest("provider", Settings, out var staleFailure, out _).Should().BeTrue();
        tracker.RecordProviderFailure("provider", Settings, "open", openingFailure);
        clock.Advance(TimeSpan.FromSeconds(60));
        tracker.TryEnterProviderRequest("provider", Settings, out var probe, out _).Should().BeTrue();

        tracker.RecordProviderSuccess("provider", probe);
        tracker.RecordProviderFailure("provider", Settings, "stale", staleFailure);

        tracker.Get("provider")!.State.Should().Be(CircuitState.Closed);
        tracker.TryEnterProviderRequest("provider", Settings, out var admitted, out _).Should().BeTrue();
        admitted.IsHalfOpenProbe.Should().BeFalse();
    }

    [Fact]
    public void CompanionSuccess_DoesNotMaskPollyOpen()
    {
        var tracker = new CircuitBreakerStateTracker(new ManualTimeProvider());
        tracker.RecordState("provider", CircuitState.Open, "polly open");
        tracker.TryEnterProviderRequest("provider", Settings, out var companion, out _).Should().BeTrue();

        tracker.RecordProviderSuccess("provider", companion);

        tracker.Get("provider")!.State.Should().Be(CircuitState.Open);
        tracker.Get("provider")!.LastFailureReason.Should().Be("polly open");
    }

    [Fact]
    public void PollyReset_DoesNotMaskCompanionOpen()
    {
        var tracker = new CircuitBreakerStateTracker(new ManualTimeProvider());
        tracker.TryEnterProviderRequest("provider", Settings, out var companion, out _).Should().BeTrue();
        tracker.RecordProviderFailure("provider", Settings, "companion open", companion);

        tracker.RecordState("provider", CircuitState.Closed);

        tracker.Get("provider")!.State.Should().Be(CircuitState.Open);
        tracker.GetAll()["provider"].LastFailureReason.Should().Be("companion open");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
