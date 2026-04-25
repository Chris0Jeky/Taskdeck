using FluentAssertions;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Confidence;

public class ConfidenceScoreTests
{
    [Fact]
    public void Constructor_ShouldCreateScore_WithValidInputs()
    {
        var score = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "LLM said 75% confident");

        score.Score.Should().Be(0.75);
        score.Source.Should().Be(ConfidenceSource.Verbalized);
        score.Explanation.Should().Be("LLM said 75% confident");
    }

    [Fact]
    public void Constructor_ShouldAcceptZero()
    {
        var score = new ConfidenceScore(0.0, ConfidenceSource.ProviderLogprob, "No confidence");

        score.Score.Should().Be(0.0);
    }

    [Fact]
    public void Constructor_ShouldAcceptOne()
    {
        var score = new ConfidenceScore(1.0, ConfidenceSource.SelfConsistency, "Full consistency");

        score.Score.Should().Be(1.0);
    }

    [Fact]
    public void Constructor_ShouldAcceptNullExplanation()
    {
        var score = new ConfidenceScore(0.5, ConfidenceSource.Verbalized, null!);

        score.Explanation.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(-1.0)]
    [InlineData(1.001)]
    [InlineData(2.0)]
    public void Constructor_ShouldThrow_WhenScoreOutOfRange(double value)
    {
        var act = () => new ConfidenceScore(value, ConfidenceSource.Verbalized, "test");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenScoreIsNaN()
    {
        var act = () => new ConfidenceScore(double.NaN, ConfidenceSource.Verbalized, "test");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenScoreIsPositiveInfinity()
    {
        var act = () => new ConfidenceScore(double.PositiveInfinity, ConfidenceSource.Verbalized, "test");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenScoreIsNegativeInfinity()
    {
        var act = () => new ConfidenceScore(double.NegativeInfinity, ConfidenceSource.Verbalized, "test");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(0.0, ConfidenceBucket.VeryLow)]
    [InlineData(0.1, ConfidenceBucket.VeryLow)]
    [InlineData(0.19999, ConfidenceBucket.VeryLow)]
    [InlineData(0.2, ConfidenceBucket.Low)]
    [InlineData(0.3, ConfidenceBucket.Low)]
    [InlineData(0.39999, ConfidenceBucket.Low)]
    [InlineData(0.4, ConfidenceBucket.Medium)]
    [InlineData(0.5, ConfidenceBucket.Medium)]
    [InlineData(0.59999, ConfidenceBucket.Medium)]
    [InlineData(0.6, ConfidenceBucket.High)]
    [InlineData(0.7, ConfidenceBucket.High)]
    [InlineData(0.79999, ConfidenceBucket.High)]
    [InlineData(0.8, ConfidenceBucket.VeryHigh)]
    [InlineData(0.9, ConfidenceBucket.VeryHigh)]
    [InlineData(1.0, ConfidenceBucket.VeryHigh)]
    public void ToBucket_ShouldMapCorrectly(double score, ConfidenceBucket expected)
    {
        var cs = new ConfidenceScore(score, ConfidenceSource.Verbalized, "test");

        cs.ToBucket().Should().Be(expected);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    public void ScoreToBucket_ShouldThrow_WhenScoreInvalid(double score)
    {
        var act = () => ConfidenceScore.ScoreToBucket(score);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForIdenticalScores()
    {
        var a = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "same");
        var b = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "same");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentScores()
    {
        var a = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "test");
        var b = new ConfidenceScore(0.80, ConfidenceSource.Verbalized, "test");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentSources()
    {
        var a = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "test");
        var b = new ConfidenceScore(0.75, ConfidenceSource.ProviderLogprob, "test");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentExplanations()
    {
        var a = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "one");
        var b = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "two");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equals_ShouldHandleNullComparison()
    {
        var a = new ConfidenceScore(0.5, ConfidenceSource.Verbalized, "test");

        a.Equals(null).Should().BeFalse();
        (a == null).Should().BeFalse();
        (null == a).Should().BeFalse();
    }

    [Fact]
    public void CompareTo_ShouldOrderByScore()
    {
        var low = new ConfidenceScore(0.2, ConfidenceSource.Verbalized, "low");
        var high = new ConfidenceScore(0.8, ConfidenceSource.Verbalized, "high");

        low.CompareTo(high).Should().BeNegative();
        high.CompareTo(low).Should().BePositive();
        low.CompareTo(low).Should().Be(0);
    }

    [Fact]
    public void CompareTo_ShouldReturnZero_WhenScoresAreEpsilonEqualAndMetadataMatches()
    {
        var a = new ConfidenceScore(0.5, ConfidenceSource.Verbalized, "same");
        var b = new ConfidenceScore(0.5 + 4e-13, ConfidenceSource.Verbalized, "same");

        a.CompareTo(b).Should().Be(0);
    }

    [Fact]
    public void CompareTo_ShouldDistinguishSignals_WhenScoresMatchButMetadataDiffers()
    {
        var verbalized = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "same");
        var provider = new ConfidenceScore(0.75, ConfidenceSource.ProviderLogprob, "same");
        var explained = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "different");

        verbalized.CompareTo(provider).Should().NotBe(0);
        verbalized.CompareTo(explained).Should().NotBe(0);
    }

    [Fact]
    public void CompareTo_ShouldHandleNull()
    {
        var score = new ConfidenceScore(0.5, ConfidenceSource.Verbalized, "test");

        score.CompareTo(null).Should().BePositive();
    }

    [Fact]
    public void GetHashCode_ShouldBeConsistentWithEquals()
    {
        var a = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "test");
        var b = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "test");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_ShouldMatch_WhenScoresAreEpsilonEqual()
    {
        var a = new ConfidenceScore(0.5 - 1e-13, ConfidenceSource.Verbalized, "test");
        var b = new ConfidenceScore(0.5 + 1e-13, ConfidenceSource.Verbalized, "test");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldContainSourceAndScore()
    {
        var score = new ConfidenceScore(0.75, ConfidenceSource.Verbalized, "some reason");

        var str = score.ToString();

        str.Should().Contain("Verbalized");
        str.Should().Contain("0.750");
        str.Should().Contain("some reason");
    }
}
