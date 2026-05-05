using FluentAssertions;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Confidence;

public class ConfidenceBreakdownTests
{
    private static IReadOnlyList<ConfidenceComponent> DefaultComponents() => new[]
    {
        new ConfidenceComponent("Pattern match", 0.9),
        new ConfidenceComponent("Reach", 0.8),
        new ConfidenceComponent("Reversibility", 0.7),
        new ConfidenceComponent("Recency", 0.6)
    };

    [Fact]
    public void Constructor_ShouldCreate_WithValidInputs()
    {
        var components = DefaultComponents();
        var breakdown = new ConfidenceBreakdown(0.75, components, "All good", 0.7);

        breakdown.Overall.Should().Be(0.75);
        breakdown.Components.Should().HaveCount(4);
        breakdown.Note.Should().Be("All good");
        breakdown.Threshold.Should().Be(0.7);
    }

    [Fact]
    public void Constructor_ShouldAcceptNullNote()
    {
        var breakdown = new ConfidenceBreakdown(0.5, DefaultComponents(), null, 0.7);
        breakdown.Note.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldAcceptEmptyComponentsList()
    {
        var breakdown = new ConfidenceBreakdown(0.5, Array.Empty<ConfidenceComponent>(), null, 0.7);
        breakdown.Components.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldDefensiveCopyComponents()
    {
        var list = new List<ConfidenceComponent>
        {
            new("A", 0.5)
        };
        var breakdown = new ConfidenceBreakdown(0.5, list, null, 0.7);

        list.Add(new ConfidenceComponent("B", 0.3));

        breakdown.Components.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(-1.0)]
    [InlineData(1.001)]
    [InlineData(2.0)]
    public void Constructor_ShouldThrow_WhenOverallOutOfRange(double overall)
    {
        var act = () => new ConfidenceBreakdown(overall, DefaultComponents(), null, 0.7);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOverallIsNaN()
    {
        var act = () => new ConfidenceBreakdown(double.NaN, DefaultComponents(), null, 0.7);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOverallIsInfinity()
    {
        var act = () => new ConfidenceBreakdown(double.PositiveInfinity, DefaultComponents(), null, 0.7);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(-1.0)]
    [InlineData(1.001)]
    [InlineData(2.0)]
    public void Constructor_ShouldThrow_WhenThresholdOutOfRange(double threshold)
    {
        var act = () => new ConfidenceBreakdown(0.5, DefaultComponents(), null, threshold);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenThresholdIsNaN()
    {
        var act = () => new ConfidenceBreakdown(0.5, DefaultComponents(), null, double.NaN);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenThresholdIsInfinity()
    {
        var act = () => new ConfidenceBreakdown(0.5, DefaultComponents(), null, double.NegativeInfinity);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenComponentsIsNull()
    {
        var act = () => new ConfidenceBreakdown(0.5, null!, null, 0.7);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldAcceptBoundaryValues()
    {
        var breakdown = new ConfidenceBreakdown(0.0, DefaultComponents(), null, 0.0);
        breakdown.Overall.Should().Be(0.0);
        breakdown.Threshold.Should().Be(0.0);

        var breakdown2 = new ConfidenceBreakdown(1.0, DefaultComponents(), null, 1.0);
        breakdown2.Overall.Should().Be(1.0);
        breakdown2.Threshold.Should().Be(1.0);
    }

    [Fact]
    public void MeetsThreshold_ShouldReturnTrue_WhenOverallEqualsThreshold()
    {
        var breakdown = new ConfidenceBreakdown(0.7, DefaultComponents(), null, 0.7);
        breakdown.MeetsThreshold.Should().BeTrue();
    }

    [Fact]
    public void MeetsThreshold_ShouldReturnTrue_WhenOverallAboveThreshold()
    {
        var breakdown = new ConfidenceBreakdown(0.8, DefaultComponents(), null, 0.7);
        breakdown.MeetsThreshold.Should().BeTrue();
    }

    [Fact]
    public void MeetsThreshold_ShouldReturnFalse_WhenOverallBelowThreshold()
    {
        var breakdown = new ConfidenceBreakdown(0.6, DefaultComponents(), null, 0.7);
        breakdown.MeetsThreshold.Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForIdenticalBreakdowns()
    {
        var a = new ConfidenceBreakdown(0.75, DefaultComponents(), "note", 0.7);
        var b = new ConfidenceBreakdown(0.75, DefaultComponents(), "note", 0.7);

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentOverall()
    {
        var a = new ConfidenceBreakdown(0.75, DefaultComponents(), null, 0.7);
        var b = new ConfidenceBreakdown(0.80, DefaultComponents(), null, 0.7);

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentThreshold()
    {
        var a = new ConfidenceBreakdown(0.75, DefaultComponents(), null, 0.7);
        var b = new ConfidenceBreakdown(0.75, DefaultComponents(), null, 0.8);

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentNote()
    {
        var a = new ConfidenceBreakdown(0.75, DefaultComponents(), "note A", 0.7);
        var b = new ConfidenceBreakdown(0.75, DefaultComponents(), "note B", 0.7);

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentComponentCount()
    {
        var a = new ConfidenceBreakdown(0.75, DefaultComponents(), null, 0.7);
        var b = new ConfidenceBreakdown(0.75, new[] { new ConfidenceComponent("A", 0.5) }, null, 0.7);

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenComparedToNull()
    {
        var a = new ConfidenceBreakdown(0.75, DefaultComponents(), null, 0.7);
        a.Equals(null).Should().BeFalse();
        (a == null).Should().BeFalse();
        (null == a).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForNullToNull()
    {
        ConfidenceBreakdown? a = null;
        ConfidenceBreakdown? b = null;
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_ShouldBeEqual_ForEqualBreakdowns()
    {
        var a = new ConfidenceBreakdown(0.75, DefaultComponents(), "note", 0.7);
        var b = new ConfidenceBreakdown(0.75, DefaultComponents(), "note", 0.7);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldContainOverallAndThreshold()
    {
        var breakdown = new ConfidenceBreakdown(0.75, DefaultComponents(), null, 0.7);
        var str = breakdown.ToString();
        str.Should().Contain("0.750");
        str.Should().Contain("0.700");
        str.Should().Contain("4"); // component count
    }
}
