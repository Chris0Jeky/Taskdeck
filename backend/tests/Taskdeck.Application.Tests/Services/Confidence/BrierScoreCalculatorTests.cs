using FluentAssertions;
using Taskdeck.Application.Services.Confidence;
using Taskdeck.Domain.Exceptions;
using Xunit;
using static Taskdeck.Application.Services.Confidence.BrierScoreCalculator;

namespace Taskdeck.Application.Tests.Services.Confidence;

public class BrierScoreCalculatorTests
{
    #region Calculate - basic cases

    [Fact]
    public void Calculate_PerfectPredictions_ShouldReturnZero()
    {
        // All predictions are exactly correct
        var predictions = new List<Prediction>
        {
            new(1.0, true),   // predicted 100%, happened
            new(0.0, false),  // predicted 0%, didn't happen
            new(1.0, true),
            new(0.0, false)
        };

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void Calculate_WorstPredictions_ShouldReturnOne()
    {
        // All predictions are maximally wrong
        var predictions = new List<Prediction>
        {
            new(0.0, true),   // predicted 0%, but happened
            new(1.0, false),  // predicted 100%, but didn't happen
        };

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void Calculate_SinglePrediction_ShouldReturnSquaredError()
    {
        // predicted 0.7, outcome true(1) → (0.7 - 1)^2 = 0.09
        var predictions = new List<Prediction>
        {
            new(0.7, true)
        };

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeApproximately(0.09, 1e-12);
    }

    [Fact]
    public void Calculate_SinglePrediction_OutcomeFalse()
    {
        // predicted 0.3, outcome false(0) → (0.3 - 0)^2 = 0.09
        var predictions = new List<Prediction>
        {
            new(0.3, false)
        };

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeApproximately(0.09, 1e-12);
    }

    #endregion

    #region Calculate - known mathematical examples

    [Fact]
    public void Calculate_KnownExample_MixedPredictions()
    {
        // Example: 4 predictions
        // (0.9, true)  → (0.9-1)^2 = 0.01
        // (0.1, false) → (0.1-0)^2 = 0.01
        // (0.8, true)  → (0.8-1)^2 = 0.04
        // (0.3, false) → (0.3-0)^2 = 0.09
        // Sum = 0.15, N=4, Brier = 0.15/4 = 0.0375
        var predictions = new List<Prediction>
        {
            new(0.9, true),
            new(0.1, false),
            new(0.8, true),
            new(0.3, false)
        };

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeApproximately(0.0375, 1e-12);
    }

    [Fact]
    public void Calculate_AllFiftyFifty_ShouldReturn025()
    {
        // Predicting 0.5 for everything: (0.5-1)^2 + (0.5-0)^2 = 0.25 + 0.25 = 0.5 / 2 = 0.25
        var predictions = new List<Prediction>
        {
            new(0.5, true),
            new(0.5, false)
        };

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeApproximately(0.25, 1e-12);
    }

    [Fact]
    public void Calculate_Uniform50Percent_AllTrue_ShouldReturn025()
    {
        // All predictions at 0.5, all outcomes true
        // Each: (0.5-1)^2 = 0.25
        var predictions = Enumerable.Range(0, 100)
            .Select(_ => new Prediction(0.5, true))
            .ToList();

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeApproximately(0.25, 1e-12);
    }

    #endregion

    #region Calculate - edge cases

    [Fact]
    public void Calculate_ShouldThrow_WhenPredictionsEmpty()
    {
        var act = () => BrierScoreCalculator.Calculate(Array.Empty<Prediction>());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Calculate_ShouldThrow_WhenPredictionsNull()
    {
        var act = () => BrierScoreCalculator.Calculate(null!);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Calculate_ShouldThrow_WhenPredictedProbabilityNegative()
    {
        var predictions = new List<Prediction>
        {
            new(-0.1, true)
        };

        var act = () => BrierScoreCalculator.Calculate(predictions);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Calculate_ShouldThrow_WhenPredictedProbabilityAboveOne()
    {
        var predictions = new List<Prediction>
        {
            new(1.1, true)
        };

        var act = () => BrierScoreCalculator.Calculate(predictions);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Calculate_ShouldThrow_WhenPredictedProbabilityIsNaN()
    {
        var predictions = new List<Prediction>
        {
            new(double.NaN, true)
        };

        var act = () => BrierScoreCalculator.Calculate(predictions);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Calculate_ShouldThrow_WhenPredictedProbabilityIsInfinity()
    {
        var predictions = new List<Prediction>
        {
            new(double.PositiveInfinity, true)
        };

        var act = () => BrierScoreCalculator.Calculate(predictions);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Calculate_BoundaryValues_ZeroAndOne()
    {
        // Predictions at exact boundaries
        var predictions = new List<Prediction>
        {
            new(0.0, false), // (0-0)^2 = 0
            new(1.0, true),  // (1-1)^2 = 0
        };

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void Calculate_ResultShouldAlwaysBeInZeroOneRange()
    {
        // Even with many wrong predictions, score should stay in [0,1]
        var predictions = Enumerable.Range(0, 1000)
            .Select(i => new Prediction(i % 2 == 0 ? 0.0 : 1.0, i % 2 != 0))
            .ToList();

        var score = BrierScoreCalculator.Calculate(predictions);

        score.Should().BeGreaterOrEqualTo(0.0);
        score.Should().BeLessOrEqualTo(1.0);
    }

    #endregion

    #region CalculateSkillScore

    [Fact]
    public void CalculateSkillScore_PerfectModel_ShouldReturnOne()
    {
        // BS = 0, BS_ref = 0.25 → BSS = 1 - 0/0.25 = 1.0
        var bss = BrierScoreCalculator.CalculateSkillScore(0.0, 0.25);

        bss.Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void CalculateSkillScore_NoSkill_ShouldReturnZero()
    {
        // BS = BS_ref → BSS = 1 - 1 = 0
        var bss = BrierScoreCalculator.CalculateSkillScore(0.25, 0.25);

        bss.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void CalculateSkillScore_WorseThanReference_ShouldBeNegative()
    {
        // BS = 0.5, BS_ref = 0.25 → BSS = 1 - 2 = -1
        var bss = BrierScoreCalculator.CalculateSkillScore(0.5, 0.25);

        bss.Should().BeApproximately(-1.0, 1e-12);
    }

    [Fact]
    public void CalculateSkillScore_ShouldThrow_WhenReferenceIsZero()
    {
        var act = () => BrierScoreCalculator.CalculateSkillScore(0.1, 0.0);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void CalculateSkillScore_ShouldThrow_WhenReferenceIsNegative()
    {
        var act = () => BrierScoreCalculator.CalculateSkillScore(0.1, -0.1);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    public void CalculateSkillScore_ShouldThrow_WhenBrierScoreOutOfRange(double brierScore)
    {
        var act = () => BrierScoreCalculator.CalculateSkillScore(brierScore, 0.25);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(1.001)]
    public void CalculateSkillScore_ShouldThrow_WhenReferenceOutOfRange(double referenceBrierScore)
    {
        var act = () => BrierScoreCalculator.CalculateSkillScore(0.25, referenceBrierScore);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void CalculateSkillScore_ShouldThrow_WhenBrierScoreIsNaN()
    {
        var act = () => BrierScoreCalculator.CalculateSkillScore(double.NaN, 0.25);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void CalculateSkillScore_ShouldThrow_WhenBrierScoreIsInfinity()
    {
        var act = () => BrierScoreCalculator.CalculateSkillScore(double.PositiveInfinity, 0.25);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void CalculateSkillScore_ShouldThrow_WhenReferenceIsNaN()
    {
        var act = () => BrierScoreCalculator.CalculateSkillScore(0.1, double.NaN);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void CalculateSkillScore_ShouldThrow_WhenReferenceIsInfinity()
    {
        var act = () => BrierScoreCalculator.CalculateSkillScore(0.1, double.PositiveInfinity);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion
}
