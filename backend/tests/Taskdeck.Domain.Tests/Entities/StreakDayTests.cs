using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class StreakDayTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Constructor_ShouldSucceed_WithValidIntensityBucket(int bucket)
    {
        var date = new DateOnly(2026, 4, 20);
        var day = new StreakDay(date, isSealed: false, intensityBucket: bucket);

        day.Date.Should().Be(date);
        day.IsSealed.Should().BeFalse();
        day.IntensityBucket.Should().Be(bucket);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(-42)]
    public void Constructor_ShouldThrow_WhenIntensityBucketOutOfRange(int bucket)
    {
        var act = () => new StreakDay(new DateOnly(2026, 4, 20), false, bucket);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("intensityBucket");
    }

    [Fact]
    public void Constructor_ShouldStoreIsSealed_WhenTrue()
    {
        var day = new StreakDay(new DateOnly(2026, 4, 20), isSealed: true, intensityBucket: 3);

        day.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var date = new DateOnly(2026, 4, 20);
        var day1 = new StreakDay(date, true, 2);
        var day2 = new StreakDay(date, true, 2);

        day1.Should().Be(day2);
        day1.Equals(day2).Should().BeTrue();
        (day1.GetHashCode() == day2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValues_ShouldNotBeEqual()
    {
        var date = new DateOnly(2026, 4, 20);
        var day1 = new StreakDay(date, false, 1);
        var day2 = new StreakDay(date, false, 2);

        day1.Should().NotBe(day2);
    }

    [Fact]
    public void Equals_DifferentSealed_ShouldNotBeEqual()
    {
        var date = new DateOnly(2026, 4, 20);
        var day1 = new StreakDay(date, true, 1);
        var day2 = new StreakDay(date, false, 1);

        day1.Should().NotBe(day2);
    }

    [Fact]
    public void Equals_DifferentDates_ShouldNotBeEqual()
    {
        var day1 = new StreakDay(new DateOnly(2026, 4, 20), false, 1);
        var day2 = new StreakDay(new DateOnly(2026, 4, 21), false, 1);

        day1.Should().NotBe(day2);
    }
}
