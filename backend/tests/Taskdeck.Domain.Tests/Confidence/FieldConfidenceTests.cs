using FluentAssertions;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Confidence;

public class FieldConfidenceTests
{
    [Fact]
    public void Constructor_ShouldCreateFieldConfidence_WithValidInputs()
    {
        var scores = new List<ConfidenceScore>
        {
            new(0.8, ConfidenceSource.Verbalized, "high confidence"),
            new(0.6, ConfidenceSource.ProviderLogprob, "moderate logprob")
        };

        var fc = new FieldConfidence("title", 0.7, scores);

        fc.FieldName.Should().Be("title");
        fc.AggregatedScore.Should().Be(0.7);
        fc.Bucket.Should().Be(ConfidenceBucket.High);
        fc.SourceBreakdown.Should().HaveCount(2);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFieldNameIsEmpty()
    {
        var act = () => new FieldConfidence("", 0.5, Array.Empty<ConfidenceScore>());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFieldNameIsWhitespace()
    {
        var act = () => new FieldConfidence("   ", 0.5, Array.Empty<ConfidenceScore>());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenScoreOutOfRange()
    {
        var act = () => new FieldConfidence("title", 1.5, Array.Empty<ConfidenceScore>());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenScoreIsNaN()
    {
        var act = () => new FieldConfidence("title", double.NaN, Array.Empty<ConfidenceScore>());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldAcceptNullBreakdown()
    {
        var fc = new FieldConfidence("title", 0.5, null!);

        fc.SourceBreakdown.Should().BeEmpty();
    }

    [Fact]
    public void Bucket_ShouldMatchAggregatedScore()
    {
        var fc = new FieldConfidence("description", 0.15, Array.Empty<ConfidenceScore>());
        fc.Bucket.Should().Be(ConfidenceBucket.VeryLow);

        fc = new FieldConfidence("description", 0.85, Array.Empty<ConfidenceScore>());
        fc.Bucket.Should().Be(ConfidenceBucket.VeryHigh);
    }

    [Fact]
    public void ToString_ShouldContainFieldNameAndScore()
    {
        var fc = new FieldConfidence("title", 0.7, Array.Empty<ConfidenceScore>());

        fc.ToString().Should().Contain("title");
        fc.ToString().Should().Contain("0.700");
        fc.ToString().Should().Contain("High");
    }
}
