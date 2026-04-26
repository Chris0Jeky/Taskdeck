using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class StreakResultTests
{
    [Fact]
    public void Constructor_ShouldSucceed_WithValidData()
    {
        var days = new List<StreakDay>
        {
            new(new DateOnly(2026, 4, 20), false, 1),
            new(new DateOnly(2026, 4, 21), false, 3),
        };

        var result = new StreakResult(days, currentStreakLength: 2, longestStreakLength: 5);

        result.Days.Should().HaveCount(2);
        result.CurrentStreakLength.Should().Be(2);
        result.LongestStreakLength.Should().Be(5);
    }

    [Fact]
    public void Constructor_ShouldSucceed_WithEmptyDays()
    {
        var result = new StreakResult(Array.Empty<StreakDay>(), 0, 0);

        result.Days.Should().BeEmpty();
        result.CurrentStreakLength.Should().Be(0);
        result.LongestStreakLength.Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldSucceed_WhenCurrentEqualsLongest()
    {
        var days = new List<StreakDay>
        {
            new(new DateOnly(2026, 4, 20), false, 2),
        };

        var result = new StreakResult(days, currentStreakLength: 3, longestStreakLength: 3);

        result.CurrentStreakLength.Should().Be(3);
        result.LongestStreakLength.Should().Be(3);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDaysIsNull()
    {
        var act = () => new StreakResult(null!, 0, 0);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCurrentStreakIsNegative()
    {
        var act = () => new StreakResult(Array.Empty<StreakDay>(), -1, 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("currentStreakLength");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLongestStreakIsNegative()
    {
        var act = () => new StreakResult(Array.Empty<StreakDay>(), 0, -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("longestStreakLength");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCurrentExceedsLongest()
    {
        var act = () => new StreakResult(Array.Empty<StreakDay>(), 5, 3);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("currentStreakLength");
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var days = new List<StreakDay>
        {
            new(new DateOnly(2026, 4, 20), false, 1),
        };

        var result1 = new StreakResult(days, 1, 1);
        var result2 = new StreakResult(days, 1, 1);

        result1.Should().Be(result2);
    }

    [Fact]
    public void Equals_DifferentStreakLengths_ShouldNotBeEqual()
    {
        var days = new List<StreakDay>
        {
            new(new DateOnly(2026, 4, 20), false, 1),
        };

        var result1 = new StreakResult(days, 1, 1);
        var result2 = new StreakResult(days, 0, 1);

        result1.Should().NotBe(result2);
    }
}
