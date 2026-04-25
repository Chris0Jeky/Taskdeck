using FluentAssertions;
using Taskdeck.Application.Services.Confidence;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Confidence;

public class ConfidenceAggregatorTests
{
    private readonly ConfidenceAggregator _aggregator = new();

    #region Aggregate - basic cases

    [Fact]
    public void Aggregate_ShouldReturnNull_WhenNoScores()
    {
        var result = _aggregator.Aggregate(Array.Empty<ConfidenceScore>());

        result.Should().BeNull();
    }

    [Fact]
    public void Aggregate_ShouldReturnNull_WhenScoresIsNull()
    {
        var result = _aggregator.Aggregate(null!);

        result.Should().BeNull();
    }

    [Fact]
    public void Aggregate_ShouldReturnScore_ForSingleInput()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.7, ConfidenceSource.Verbalized, "test")
        };

        var result = _aggregator.Aggregate(scores);

        result.Should().NotBeNull();
        result!.Score.Should().BeApproximately(0.7, 1e-12);
        result.Bucket.Should().Be(ConfidenceBucket.High);
        result.ContributingScores.Should().HaveCount(1);
    }

    [Fact]
    public void Aggregate_ShouldReturnEqualWeightedAverage_WhenNoWeightsProvided()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.6, ConfidenceSource.Verbalized, "a"),
            new(0.8, ConfidenceSource.ProviderLogprob, "b")
        };

        var result = _aggregator.Aggregate(scores);

        result.Should().NotBeNull();
        // (0.6 + 0.8) / 2 = 0.7
        result!.Score.Should().BeApproximately(0.7, 1e-12);
        result.ContributingScores.Should().HaveCount(2);
    }

    [Fact]
    public void Aggregate_ShouldReturnEqualWeightedAverage_WithThreeSources()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.3, ConfidenceSource.Verbalized, "a"),
            new(0.6, ConfidenceSource.ProviderLogprob, "b"),
            new(0.9, ConfidenceSource.SelfConsistency, "c")
        };

        var result = _aggregator.Aggregate(scores);

        result.Should().NotBeNull();
        // (0.3 + 0.6 + 0.9) / 3 = 0.6
        result!.Score.Should().BeApproximately(0.6, 1e-12);
    }

    #endregion

    #region Aggregate - weighted cases

    [Fact]
    public void Aggregate_ShouldApplyWeights_WhenProvided()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.4, ConfidenceSource.Verbalized, "a"),
            new(0.8, ConfidenceSource.ProviderLogprob, "b")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            { ConfidenceSource.Verbalized, 1.0 },
            { ConfidenceSource.ProviderLogprob, 3.0 }
        };

        var result = _aggregator.Aggregate(scores, weights);

        result.Should().NotBeNull();
        // (0.4*1 + 0.8*3) / (1+3) = (0.4 + 2.4) / 4 = 2.8 / 4 = 0.7
        result!.Score.Should().BeApproximately(0.7, 1e-12);
    }

    [Fact]
    public void Aggregate_ShouldExcludeScores_WithZeroWeight()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.2, ConfidenceSource.Verbalized, "low quality"),
            new(0.9, ConfidenceSource.ProviderLogprob, "high quality")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            { ConfidenceSource.Verbalized, 0.0 },
            { ConfidenceSource.ProviderLogprob, 1.0 }
        };

        var result = _aggregator.Aggregate(scores, weights);

        result.Should().NotBeNull();
        result!.Score.Should().BeApproximately(0.9, 1e-12);
        result.ContributingScores.Should().HaveCount(1);
    }

    [Fact]
    public void Aggregate_ShouldExcludeScores_WithMissingWeightKey()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.2, ConfidenceSource.Verbalized, "excluded"),
            new(0.8, ConfidenceSource.ProviderLogprob, "included")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            // Only ProviderLogprob has a weight; Verbalized is excluded
            { ConfidenceSource.ProviderLogprob, 2.0 }
        };

        var result = _aggregator.Aggregate(scores, weights);

        result.Should().NotBeNull();
        result!.Score.Should().BeApproximately(0.8, 1e-12);
        result.ContributingScores.Should().HaveCount(1);
    }

    [Fact]
    public void Aggregate_ShouldReturnNull_WhenAllWeightsAreZero()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.5, ConfidenceSource.Verbalized, "a"),
            new(0.7, ConfidenceSource.ProviderLogprob, "b")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            { ConfidenceSource.Verbalized, 0.0 },
            { ConfidenceSource.ProviderLogprob, 0.0 }
        };

        var result = _aggregator.Aggregate(scores, weights);

        result.Should().BeNull();
    }

    [Fact]
    public void Aggregate_ShouldThrow_WhenWeightIsNegative()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.5, ConfidenceSource.Verbalized, "a")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            { ConfidenceSource.Verbalized, -1.0 }
        };

        var act = () => _aggregator.Aggregate(scores, weights);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Aggregate_ShouldThrow_WhenWeightIsNaN()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.5, ConfidenceSource.Verbalized, "a")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            { ConfidenceSource.Verbalized, double.NaN }
        };

        var act = () => _aggregator.Aggregate(scores, weights);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Aggregate_ShouldThrow_WhenWeightIsInfinity()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.5, ConfidenceSource.Verbalized, "a")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            { ConfidenceSource.Verbalized, double.PositiveInfinity }
        };

        var act = () => _aggregator.Aggregate(scores, weights);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Aggregate_ShouldHandleEmptyWeightsDictionary_AsEqualWeights()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.4, ConfidenceSource.Verbalized, "a"),
            new(0.8, ConfidenceSource.ProviderLogprob, "b")
        };

        var result = _aggregator.Aggregate(scores, new Dictionary<ConfidenceSource, double>());

        result.Should().NotBeNull();
        // Empty weights dict → equal weighting
        result!.Score.Should().BeApproximately(0.6, 1e-12);
    }

    #endregion

    #region Aggregate - bucket assignment

    [Fact]
    public void Aggregate_ShouldAssignCorrectBucket()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.1, ConfidenceSource.Verbalized, "very low")
        };

        var result = _aggregator.Aggregate(scores);

        result.Should().NotBeNull();
        result!.Bucket.Should().Be(ConfidenceBucket.VeryLow);
    }

    #endregion

    #region Aggregate - duplicate sources

    [Fact]
    public void Aggregate_ShouldHandleMultipleScoresFromSameSource()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.5, ConfidenceSource.Verbalized, "first pass"),
            new(0.7, ConfidenceSource.Verbalized, "second pass")
        };

        var result = _aggregator.Aggregate(scores);

        result.Should().NotBeNull();
        // Both treated equally: (0.5 + 0.7) / 2 = 0.6
        result!.Score.Should().BeApproximately(0.6, 1e-12);
        result.ContributingScores.Should().HaveCount(2);
    }

    #endregion

    #region AggregateForField

    [Fact]
    public void AggregateForField_ShouldReturnFieldConfidence_WithAggregatedScore()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.6, ConfidenceSource.Verbalized, "a"),
            new(0.8, ConfidenceSource.ProviderLogprob, "b")
        };

        var fc = _aggregator.AggregateForField("title", scores);

        fc.FieldName.Should().Be("title");
        fc.AggregatedScore.Should().BeApproximately(0.7, 1e-12);
        fc.Bucket.Should().Be(ConfidenceBucket.High);
        fc.SourceBreakdown.Should().HaveCount(2);
    }

    [Fact]
    public void AggregateForField_ShouldReturnZeroScore_WhenNoValidScores()
    {
        var scores = Array.Empty<ConfidenceScore>();

        var fc = _aggregator.AggregateForField("title", scores);

        fc.FieldName.Should().Be("title");
        fc.AggregatedScore.Should().Be(0.0);
        fc.Bucket.Should().Be(ConfidenceBucket.VeryLow);
        fc.SourceBreakdown.Should().BeEmpty();
    }

    [Fact]
    public void AggregateForField_ShouldReturnZeroScore_WhenAllWeightsAreZero()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.9, ConfidenceSource.Verbalized, "ignored")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            { ConfidenceSource.Verbalized, 0.0 }
        };

        var fc = _aggregator.AggregateForField("title", scores, weights);

        fc.AggregatedScore.Should().Be(0.0);
    }

    #endregion

    #region Aggregate - floating-point precision

    [Fact]
    public void Aggregate_ShouldClampResultToValidRange()
    {
        // All scores at 1.0 should stay at 1.0 even with floating-point operations
        var scores = new List<ConfidenceScore>
        {
            new(1.0, ConfidenceSource.Verbalized, "a"),
            new(1.0, ConfidenceSource.ProviderLogprob, "b"),
            new(1.0, ConfidenceSource.SelfConsistency, "c")
        };

        var result = _aggregator.Aggregate(scores);

        result.Should().NotBeNull();
        result!.Score.Should().BeLessOrEqualTo(1.0);
        result.Score.Should().BeGreaterOrEqualTo(0.0);
    }

    [Fact]
    public void Aggregate_ShouldHandleVerySmallWeights()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.5, ConfidenceSource.Verbalized, "a"),
            new(0.5, ConfidenceSource.ProviderLogprob, "b")
        };

        var weights = new Dictionary<ConfidenceSource, double>
        {
            { ConfidenceSource.Verbalized, 1e-15 },
            { ConfidenceSource.ProviderLogprob, 1e-15 }
        };

        var result = _aggregator.Aggregate(scores, weights);

        result.Should().NotBeNull();
        result!.Score.Should().BeApproximately(0.5, 1e-6);
    }

    #endregion
}
