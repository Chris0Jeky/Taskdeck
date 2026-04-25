using FluentAssertions;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Confidence;

public class ConfidenceComponentTests
{
    [Fact]
    public void Constructor_ShouldCreate_WithValidInputs()
    {
        var component = new ConfidenceComponent("Pattern match", 0.85);

        component.Key.Should().Be("Pattern match");
        component.Value.Should().Be(0.85);
    }

    [Fact]
    public void Constructor_ShouldAcceptZero()
    {
        var component = new ConfidenceComponent("Reach", 0.0);
        component.Value.Should().Be(0.0);
    }

    [Fact]
    public void Constructor_ShouldAcceptOne()
    {
        var component = new ConfidenceComponent("Reversibility", 1.0);
        component.Value.Should().Be(1.0);
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(-1.0)]
    [InlineData(1.001)]
    [InlineData(2.0)]
    [InlineData(100.0)]
    public void Constructor_ShouldThrow_WhenValueOutOfRange(double value)
    {
        var act = () => new ConfidenceComponent("test", value);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenValueIsNaN()
    {
        var act = () => new ConfidenceComponent("test", double.NaN);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenValueIsPositiveInfinity()
    {
        var act = () => new ConfidenceComponent("test", double.PositiveInfinity);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenValueIsNegativeInfinity()
    {
        var act = () => new ConfidenceComponent("test", double.NegativeInfinity);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenKeyIsEmpty(string? key)
    {
        var act = () => new ConfidenceComponent(key!, 0.5);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForIdenticalComponents()
    {
        var a = new ConfidenceComponent("Reach", 0.75);
        var b = new ConfidenceComponent("Reach", 0.75);

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentKeys()
    {
        var a = new ConfidenceComponent("Reach", 0.75);
        var b = new ConfidenceComponent("Recency", 0.75);

        a.Equals(b).Should().BeFalse();
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentValues()
    {
        var a = new ConfidenceComponent("Reach", 0.75);
        var b = new ConfidenceComponent("Reach", 0.80);

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenComparedToNull()
    {
        var a = new ConfidenceComponent("Reach", 0.75);

        a.Equals(null).Should().BeFalse();
        (a == null).Should().BeFalse();
        (null == a).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForNullToNull()
    {
        ConfidenceComponent? a = null;
        ConfidenceComponent? b = null;

        (a == b).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_ShouldBeEqual_ForEqualComponents()
    {
        var a = new ConfidenceComponent("Reach", 0.75);
        var b = new ConfidenceComponent("Reach", 0.75);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldContainKeyAndValue()
    {
        var component = new ConfidenceComponent("Reach", 0.75);
        component.ToString().Should().Contain("Reach");
        component.ToString().Should().Contain("0.750");
    }
}
