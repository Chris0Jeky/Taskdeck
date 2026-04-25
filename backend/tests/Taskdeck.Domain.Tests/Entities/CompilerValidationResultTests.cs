using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CompilerValidationResultTests
{
    [Fact]
    public void Success_WithNoRisks_ShouldBeValid()
    {
        var result = CompilerValidationResult.Success();

        result.IsValid.Should().BeTrue();
        result.Risks.Should().BeEmpty();
        result.Failures.Should().BeEmpty();
        result.AggregateRiskLevel.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void Success_WithRisks_ShouldBeValidWithRiskWarnings()
    {
        var risks = new List<OperationRisk>
        {
            new(RiskLevel.Medium, "Moves card to Done column"),
            new(RiskLevel.Low, "Updates card title")
        };

        var result = CompilerValidationResult.Success(risks);

        result.IsValid.Should().BeTrue();
        result.Risks.Should().HaveCount(2);
        result.Failures.Should().BeEmpty();
        result.AggregateRiskLevel.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void Failure_WithFailures_ShouldBeInvalid()
    {
        var failures = new List<UnsupportedOperationFailure>
        {
            new("delete_board", "board", "Board deletion is not supported via proposals")
        };

        var result = CompilerValidationResult.Failure(failures);

        result.IsValid.Should().BeFalse();
        result.Failures.Should().HaveCount(1);
        result.Risks.Should().BeEmpty();
    }

    [Fact]
    public void Failure_WithFailuresAndRisks_ShouldBeInvalidWithBoth()
    {
        var failures = new List<UnsupportedOperationFailure>
        {
            new("unknown_action", "card", "Unrecognized action type")
        };
        var risks = new List<OperationRisk>
        {
            new(RiskLevel.High, "Contains destructive operations")
        };

        var result = CompilerValidationResult.Failure(failures, risks);

        result.IsValid.Should().BeFalse();
        result.Failures.Should().HaveCount(1);
        result.Risks.Should().HaveCount(1);
        result.AggregateRiskLevel.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Failure_ShouldThrow_WhenFailuresAreEmpty()
    {
        var act = () => CompilerValidationResult.Failure(
            new List<UnsupportedOperationFailure>());

        act.Should().Throw<ArgumentException>()
            .WithMessage("Failures must not be empty for a failed result.*");
    }

    [Fact]
    public void Failure_ShouldThrow_WhenFailuresAreNull()
    {
        var act = () => CompilerValidationResult.Failure(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AggregateRiskLevel_ShouldReturnHighestRisk()
    {
        var risks = new List<OperationRisk>
        {
            new(RiskLevel.Low, "Minor change"),
            new(RiskLevel.Critical, "Deletes data"),
            new(RiskLevel.Medium, "Moves multiple cards")
        };

        var result = CompilerValidationResult.Success(risks);

        result.AggregateRiskLevel.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void AggregateRiskLevel_SingleRisk_ReturnsThatLevel()
    {
        var risks = new List<OperationRisk>
        {
            new(RiskLevel.High, "Bulk operation")
        };

        var result = CompilerValidationResult.Success(risks);

        result.AggregateRiskLevel.Should().Be(RiskLevel.High);
    }
}
