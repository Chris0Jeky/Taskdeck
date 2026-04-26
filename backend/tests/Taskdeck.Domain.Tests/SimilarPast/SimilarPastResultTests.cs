using FluentAssertions;
using Taskdeck.Domain.SimilarPast;
using Xunit;

namespace Taskdeck.Domain.Tests.SimilarPast;

public class SimilarPastResultTests
{
    [Fact]
    public void Empty_ShouldHaveNoDecisionsAndZeroRate()
    {
        var empty = SimilarPastResult.Empty;

        empty.Decisions.Should().BeEmpty();
        empty.ApplyRate.Should().Be(0.0);
    }

    [Fact]
    public void ComputeApplyRate_ShouldReturnZero_WhenNoHistory()
    {
        var rate = SimilarPastResult.ComputeApplyRate(0, 0);

        rate.Should().Be(0.0);
    }

    [Fact]
    public void ComputeApplyRate_ShouldReturnOne_WhenAllApplied()
    {
        var rate = SimilarPastResult.ComputeApplyRate(5, 0);

        rate.Should().Be(1.0);
    }

    [Fact]
    public void ComputeApplyRate_ShouldReturnZero_WhenAllRejected()
    {
        var rate = SimilarPastResult.ComputeApplyRate(0, 5);

        rate.Should().Be(0.0);
    }

    [Fact]
    public void ComputeApplyRate_ShouldReturnCorrectRate_WhenMixed()
    {
        var rate = SimilarPastResult.ComputeApplyRate(3, 7);

        rate.Should().BeApproximately(0.3, 0.001);
    }

    [Fact]
    public void ComputeApplyRate_ShouldReturnHalf_WhenEqualCounts()
    {
        var rate = SimilarPastResult.ComputeApplyRate(4, 4);

        rate.Should().Be(0.5);
    }

    [Fact]
    public void ComputeApplyRate_ShouldThrow_WhenAppliedCountIsNegative()
    {
        var act = () => SimilarPastResult.ComputeApplyRate(-1, 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Applied count*");
    }

    [Fact]
    public void ComputeApplyRate_ShouldThrow_WhenRejectedCountIsNegative()
    {
        var act = () => SimilarPastResult.ComputeApplyRate(0, -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Rejected count*");
    }

    [Fact]
    public void Constructor_ShouldAcceptDecisionsAndRate()
    {
        var decisions = new[]
        {
            SimilarPastDecision.Create("#001", "First", PastVerdict.Applied, "wk 10"),
            SimilarPastDecision.Create("#002", "Second", PastVerdict.Rejected, "wk 11"),
        };

        var result = new SimilarPastResult(decisions, 0.75);

        result.Decisions.Should().HaveCount(2);
        result.ApplyRate.Should().Be(0.75);
    }
}
