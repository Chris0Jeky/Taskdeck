using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class OperationRiskTests
{
    [Fact]
    public void Constructor_ShouldCreateRisk_WithValidData()
    {
        var risk = new OperationRisk(RiskLevel.Medium, "Card will be moved to Done");

        risk.Level.Should().Be(RiskLevel.Medium);
        risk.Reason.Should().Be("Card will be moved to Done");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenReasonIsBlank(string reason)
    {
        var act = () => new OperationRisk(RiskLevel.Low, reason);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Risk reason cannot be empty.*");
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var risk1 = new OperationRisk(RiskLevel.High, "Destructive");
        var risk2 = new OperationRisk(RiskLevel.High, "Destructive");

        risk1.Should().Be(risk2);
        risk1.Equals(risk2).Should().BeTrue();
        (risk1.GetHashCode() == risk2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentLevel_ShouldNotBeEqual()
    {
        var risk1 = new OperationRisk(RiskLevel.Low, "Same reason");
        var risk2 = new OperationRisk(RiskLevel.High, "Same reason");

        risk1.Should().NotBe(risk2);
    }

    [Fact]
    public void Equals_DifferentReason_ShouldNotBeEqual()
    {
        var risk1 = new OperationRisk(RiskLevel.Medium, "Reason A");
        var risk2 = new OperationRisk(RiskLevel.Medium, "Reason B");

        risk1.Should().NotBe(risk2);
    }

    [Fact]
    public void Equals_Null_ShouldNotBeEqual()
    {
        var risk = new OperationRisk(RiskLevel.Low, "something");

        risk.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        var risk = new OperationRisk(RiskLevel.Critical, "Data loss");

        risk.ToString().Should().Be("Critical: Data loss");
    }

    [Theory]
    [InlineData(RiskLevel.Low)]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Critical)]
    public void Constructor_ShouldAcceptAllRiskLevels(RiskLevel level)
    {
        var risk = new OperationRisk(level, "test reason");

        risk.Level.Should().Be(level);
    }
}
