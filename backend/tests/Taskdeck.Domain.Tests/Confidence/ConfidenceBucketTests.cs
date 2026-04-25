using FluentAssertions;
using Taskdeck.Domain.Confidence;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Domain.Tests.Confidence;

public class ConfidenceBucketTests
{
    [Fact]
    public void ScoreToBucket_ShouldCoverEntireRange_WithNoGaps()
    {
        // Verify every 0.01 step from 0.00 to 1.00 maps to exactly one bucket
        for (double d = 0.0; d <= 1.0; d += 0.01)
        {
            var bucket = ConfidenceScore.ScoreToBucket(d);
            Enum.IsDefined(typeof(ConfidenceBucket), bucket).Should().BeTrue(
                $"Score {d:F2} should map to a defined bucket, but got {bucket}");
        }
    }

    [Fact]
    public void ScoreToBucket_BoundaryAt0_2_ShouldTransitionFromVeryLowToLow()
    {
        ConfidenceScore.ScoreToBucket(0.19999999999).Should().Be(ConfidenceBucket.VeryLow);
        ConfidenceScore.ScoreToBucket(0.2).Should().Be(ConfidenceBucket.Low);
    }

    [Fact]
    public void ScoreToBucket_BoundaryAt0_4_ShouldTransitionFromLowToMedium()
    {
        ConfidenceScore.ScoreToBucket(0.39999999999).Should().Be(ConfidenceBucket.Low);
        ConfidenceScore.ScoreToBucket(0.4).Should().Be(ConfidenceBucket.Medium);
    }

    [Fact]
    public void ScoreToBucket_BoundaryAt0_6_ShouldTransitionFromMediumToHigh()
    {
        ConfidenceScore.ScoreToBucket(0.59999999999).Should().Be(ConfidenceBucket.Medium);
        ConfidenceScore.ScoreToBucket(0.6).Should().Be(ConfidenceBucket.High);
    }

    [Fact]
    public void ScoreToBucket_BoundaryAt0_8_ShouldTransitionFromHighToVeryHigh()
    {
        ConfidenceScore.ScoreToBucket(0.79999999999).Should().Be(ConfidenceBucket.High);
        ConfidenceScore.ScoreToBucket(0.8).Should().Be(ConfidenceBucket.VeryHigh);
    }

    [Fact]
    public void ScoreToBucket_ExactBoundaries_ShouldBeUpperBucket()
    {
        // Each boundary value belongs to the upper bucket (lower bound inclusive)
        ConfidenceScore.ScoreToBucket(0.0).Should().Be(ConfidenceBucket.VeryLow);
        ConfidenceScore.ScoreToBucket(0.2).Should().Be(ConfidenceBucket.Low);
        ConfidenceScore.ScoreToBucket(0.4).Should().Be(ConfidenceBucket.Medium);
        ConfidenceScore.ScoreToBucket(0.6).Should().Be(ConfidenceBucket.High);
        ConfidenceScore.ScoreToBucket(0.8).Should().Be(ConfidenceBucket.VeryHigh);
        ConfidenceScore.ScoreToBucket(1.0).Should().Be(ConfidenceBucket.VeryHigh);
    }

    [Fact]
    public void Buckets_ShouldHaveNoOverlaps()
    {
        // For a dense sweep, each score maps to exactly one bucket, and transitions are monotonic
        ConfidenceBucket? previous = null;
        int transitionCount = 0;

        for (double d = 0.0; d <= 1.0; d += 0.001)
        {
            var bucket = ConfidenceScore.ScoreToBucket(d);

            if (previous.HasValue && bucket != previous.Value)
            {
                // Bucket should only increase (monotonic transitions)
                ((int)bucket).Should().BeGreaterThan((int)previous.Value,
                    $"Bucket should increase monotonically at score {d:F3}");
                transitionCount++;
            }

            previous = bucket;
        }

        // Exactly 4 transitions: VeryLow→Low, Low→Medium, Medium→High, High→VeryHigh
        transitionCount.Should().Be(4);
    }

    [Fact]
    public void ConfidenceBucket_EnumValues_ShouldBeSequential()
    {
        ((int)ConfidenceBucket.VeryLow).Should().Be(0);
        ((int)ConfidenceBucket.Low).Should().Be(1);
        ((int)ConfidenceBucket.Medium).Should().Be(2);
        ((int)ConfidenceBucket.High).Should().Be(3);
        ((int)ConfidenceBucket.VeryHigh).Should().Be(4);
    }

    [Fact]
    public void ConfidenceBucket_ShouldHaveExactlyFiveValues()
    {
        Enum.GetValues<ConfidenceBucket>().Should().HaveCount(5);
    }
}
