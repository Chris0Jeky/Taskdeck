using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ReversibilityTests
{
    [Fact]
    public void DefaultWindowMs_ShouldBe6Hours()
    {
        Reversibility.DefaultWindowMs.Should().Be(21_600_000L);
    }

    [Fact]
    public void Constructor_ShouldCreateReversibility_WithValidData()
    {
        var rev = new Reversibility("6 hours · single keystroke", "Fully reversible.", Reversibility.DefaultWindowMs);

        rev.Summary.Should().Be("6 hours · single keystroke");
        rev.Description.Should().Be("Fully reversible.");
        rev.WindowMs.Should().Be(21_600_000L);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenSummaryIsBlank(string summary)
    {
        var act = () => new Reversibility(summary, "Description", Reversibility.DefaultWindowMs);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Reversibility summary cannot be empty.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenDescriptionIsBlank(string description)
    {
        var act = () => new Reversibility("Summary", description, Reversibility.DefaultWindowMs);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Reversibility description cannot be empty.*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_ShouldThrow_WhenWindowMsIsNotPositive(long windowMs)
    {
        var act = () => new Reversibility("Summary", "Description", windowMs);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Reversibility window must be positive.*");
    }

    [Fact]
    public void Constructor_ShouldAcceptCustomWindowMs()
    {
        var rev = new Reversibility("3 hours", "Tight window.", 10_800_000L);

        rev.WindowMs.Should().Be(10_800_000L);
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var rev1 = new Reversibility("S", "D", 1000);
        var rev2 = new Reversibility("S", "D", 1000);

        rev1.Should().Be(rev2);
        rev1.Equals(rev2).Should().BeTrue();
        (rev1.GetHashCode() == rev2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentSummary_ShouldNotBeEqual()
    {
        var rev1 = new Reversibility("Summary A", "Same desc", 1000);
        var rev2 = new Reversibility("Summary B", "Same desc", 1000);

        rev1.Should().NotBe(rev2);
    }

    [Fact]
    public void Equals_DifferentDescription_ShouldNotBeEqual()
    {
        var rev1 = new Reversibility("Same", "Desc A", 1000);
        var rev2 = new Reversibility("Same", "Desc B", 1000);

        rev1.Should().NotBe(rev2);
    }

    [Fact]
    public void Equals_DifferentWindowMs_ShouldNotBeEqual()
    {
        var rev1 = new Reversibility("Same", "Same", 1000);
        var rev2 = new Reversibility("Same", "Same", 2000);

        rev1.Should().NotBe(rev2);
    }

    [Fact]
    public void Equals_Null_ShouldNotBeEqual()
    {
        var rev = new Reversibility("S", "D", 1000);

        rev.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        var rev = new Reversibility("6 hours", "Description", 21_600_000L);

        rev.ToString().Should().Be("6 hours (21600000ms)");
    }
}
