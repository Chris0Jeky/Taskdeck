using FluentAssertions;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Confidence;

public class SelfConsistencyPolicyTests
{
    [Fact]
    public void Constructor_ShouldCreatePolicy_WithValidInputs()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.Medium, 0.3, 5);

        policy.CriticalityThreshold.Should().Be(ConfidenceBucket.Medium);
        policy.ConfidenceFloor.Should().Be(0.3);
        policy.GenerationCount.Should().Be(5);
    }

    [Fact]
    public void Constructor_ShouldDefaultGenerationCountToThree()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.High, 0.5);

        policy.GenerationCount.Should().Be(3);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenConfidenceFloorOutOfRange()
    {
        var act = () => new SelfConsistencyPolicy(ConfidenceBucket.Medium, 1.5);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenConfidenceFloorIsNaN()
    {
        var act = () => new SelfConsistencyPolicy(ConfidenceBucket.Medium, double.NaN);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenGenerationCountLessThanTwo()
    {
        var act = () => new SelfConsistencyPolicy(ConfidenceBucket.Medium, 0.3, 1);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCriticalityThresholdUndefined()
    {
        var act = () => new SelfConsistencyPolicy((ConfidenceBucket)999, 0.3);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void ShouldTrigger_ShouldReturnTrue_WhenCriticalityMeetsThreshold()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.Medium, 0.3);

        // Medium criticality meets Medium threshold
        policy.ShouldTrigger(ConfidenceBucket.Medium, 0.9).Should().BeTrue();
    }

    [Fact]
    public void ShouldTrigger_ShouldReturnTrue_WhenCriticalityExceedsThreshold()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.Medium, 0.3);

        // High criticality exceeds Medium threshold
        policy.ShouldTrigger(ConfidenceBucket.High, 0.9).Should().BeTrue();
    }

    [Fact]
    public void ShouldTrigger_ShouldReturnTrue_WhenConfidenceBelowFloor()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.High, 0.5);

        // Low criticality but confidence below floor
        policy.ShouldTrigger(ConfidenceBucket.VeryLow, 0.3).Should().BeTrue();
    }

    [Fact]
    public void ShouldTrigger_ShouldReturnFalse_WhenBothConditionsNotMet()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.High, 0.3);

        // Low criticality and high confidence
        policy.ShouldTrigger(ConfidenceBucket.Low, 0.8).Should().BeFalse();
    }

    [Fact]
    public void ShouldTrigger_ShouldReturnFalse_WhenConfidenceExactlyAtFloor()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.VeryHigh, 0.5);

        // Confidence exactly at floor should NOT trigger (floor is exclusive lower bound)
        policy.ShouldTrigger(ConfidenceBucket.VeryLow, 0.5).Should().BeFalse();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    public void ShouldTrigger_ShouldThrow_WhenMinimumFieldConfidenceInvalid(double confidence)
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.VeryHigh, 0.5);

        var act = () => policy.ShouldTrigger(ConfidenceBucket.VeryLow, confidence);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void ShouldTrigger_ShouldThrow_WhenProposalCriticalityUndefined()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.VeryHigh, 0.5);

        var act = () => policy.ShouldTrigger((ConfidenceBucket)999, 0.5);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void ShouldTrigger_ShouldReturnTrue_WhenConfidenceJustBelowFloor()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.VeryHigh, 0.5);

        policy.ShouldTrigger(ConfidenceBucket.VeryLow, 0.4999).Should().BeTrue();
    }

    [Fact]
    public void ToString_ShouldContainPolicyDetails()
    {
        var policy = new SelfConsistencyPolicy(ConfidenceBucket.Medium, 0.3, 5);

        var str = policy.ToString();
        str.Should().Contain("Medium");
        str.Should().Contain("0.30");
        str.Should().Contain("5");
    }
}
