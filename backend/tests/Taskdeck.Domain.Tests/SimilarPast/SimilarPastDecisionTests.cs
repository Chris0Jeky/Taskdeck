using FluentAssertions;
using Taskdeck.Domain.SimilarPast;
using Xunit;

namespace Taskdeck.Domain.Tests.SimilarPast;

public class SimilarPastDecisionTests
{
    [Fact]
    public void Create_ShouldCreateDecision_WithValidParameters()
    {
        var decision = SimilarPastDecision.Create("#001", "Move card to Done", PastVerdict.Applied, "wk 14");

        decision.Serial.Should().Be("#001");
        decision.Title.Should().Be("Move card to Done");
        decision.Verdict.Should().Be(PastVerdict.Applied);
        decision.Date.Should().Be("wk 14");
    }

    [Fact]
    public void Create_ShouldAcceptRejectedVerdict()
    {
        var decision = SimilarPastDecision.Create("#002", "Archive old cards", PastVerdict.Rejected, "wk 10");

        decision.Verdict.Should().Be(PastVerdict.Rejected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Create_ShouldThrow_WhenSerialIsEmpty(string? serial)
    {
        var act = () => SimilarPastDecision.Create(serial!, "title", PastVerdict.Applied, "wk 1");

        act.Should().Throw<ArgumentException>().WithMessage("*Serial*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Create_ShouldThrow_WhenTitleIsEmpty(string? title)
    {
        var act = () => SimilarPastDecision.Create("#001", title!, PastVerdict.Applied, "wk 1");

        act.Should().Throw<ArgumentException>().WithMessage("*Title*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Create_ShouldThrow_WhenDateIsEmpty(string? date)
    {
        var act = () => SimilarPastDecision.Create("#001", "title", PastVerdict.Applied, date!);

        act.Should().Throw<ArgumentException>().WithMessage("*Date*");
    }

    [Fact]
    public void Create_ShouldTruncateTitle_WhenExceedsMaxLength()
    {
        var longTitle = new string('x', SimilarPastDecision.MaxTitleLength + 50);

        var decision = SimilarPastDecision.Create("#001", longTitle, PastVerdict.Applied, "wk 1");

        decision.Title.Length.Should().Be(SimilarPastDecision.MaxTitleLength);
    }

    [Fact]
    public void Create_ShouldNotTruncateTitle_WhenWithinMaxLength()
    {
        var title = "Short title";

        var decision = SimilarPastDecision.Create("#001", title, PastVerdict.Applied, "wk 1");

        decision.Title.Should().Be(title);
    }

    [Fact]
    public void Record_Equality_ShouldWork()
    {
        var a = SimilarPastDecision.Create("#001", "Title", PastVerdict.Applied, "wk 14");
        var b = SimilarPastDecision.Create("#001", "Title", PastVerdict.Applied, "wk 14");

        a.Should().Be(b);
    }

    [Fact]
    public void Record_Inequality_ShouldWorkForDifferentVerdicts()
    {
        var a = SimilarPastDecision.Create("#001", "Title", PastVerdict.Applied, "wk 14");
        var b = SimilarPastDecision.Create("#001", "Title", PastVerdict.Rejected, "wk 14");

        a.Should().NotBe(b);
    }
}
