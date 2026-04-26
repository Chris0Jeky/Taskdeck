using FluentAssertions;
using Taskdeck.Domain.Common;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CadenceBucketTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 5)]
    [InlineData(23, 100)]
    public void Constructor_ValidInputs_CreatesBucket(int hour, int eventCount)
    {
        var bucket = new CadenceBucket(hour, eventCount);

        bucket.Hour.Should().Be(hour);
        bucket.EventCount.Should().Be(eventCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(100)]
    public void Constructor_InvalidHour_Throws(int hour)
    {
        var act = () => new CadenceBucket(hour, 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("hour");
    }

    [Fact]
    public void Constructor_NegativeEventCount_Throws()
    {
        var act = () => new CadenceBucket(0, -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("eventCount");
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new CadenceBucket(10, 5);
        var b = new CadenceBucket(10, 5);

        a.Should().Be(b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = new CadenceBucket(10, 5);
        var b = new CadenceBucket(10, 6);

        a.Should().NotBe(b);
    }
}

public class CadenceSnapshotTests
{
    [Fact]
    public void Empty_Returns24Buckets_AllZero()
    {
        var snapshot = CadenceSnapshot.Empty();

        snapshot.Buckets.Should().HaveCount(24);
        snapshot.Buckets.Select(b => b.Hour).Should().BeEquivalentTo(Enumerable.Range(0, 24));
        snapshot.Buckets.Should().OnlyContain(b => b.EventCount == 0);
        snapshot.FirstActionAt.Should().BeNull();
        snapshot.PeakHour.Should().BeNull();
        snapshot.LastActionAt.Should().BeNull();
    }

    [Fact]
    public void Empty_ReturnsSameInstance()
    {
        var a = CadenceSnapshot.Empty();
        var b = CadenceSnapshot.Empty();

        ReferenceEquals(a, b).Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullBuckets_Throws()
    {
        var act = () => new CadenceSnapshot(null!, null, null, null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WrongBucketCount_Throws()
    {
        var buckets = Enumerable.Range(0, 12)
            .Select(h => new CadenceBucket(h, 0))
            .ToList()
            .AsReadOnly();

        var act = () => new CadenceSnapshot(buckets, null, null, null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*exactly 24*");
    }

    [Fact]
    public void Constructor_SingleEvent_SetsAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var buckets = Enumerable.Range(0, 24)
            .Select(h => new CadenceBucket(h, h == 14 ? 1 : 0))
            .ToList()
            .AsReadOnly();

        var snapshot = new CadenceSnapshot(buckets, now, 14, now);

        snapshot.Buckets.Should().HaveCount(24);
        snapshot.Buckets[14].EventCount.Should().Be(1);
        snapshot.FirstActionAt.Should().Be(now);
        snapshot.PeakHour.Should().Be(14);
        snapshot.LastActionAt.Should().Be(now);
    }

    [Fact]
    public void Constructor_AllHoursFilled_PreservesData()
    {
        var first = new DateTimeOffset(2026, 4, 25, 0, 5, 0, TimeSpan.Zero);
        var last = new DateTimeOffset(2026, 4, 25, 23, 55, 0, TimeSpan.Zero);

        var buckets = Enumerable.Range(0, 24)
            .Select(h => new CadenceBucket(h, h + 1))
            .ToList()
            .AsReadOnly();

        var snapshot = new CadenceSnapshot(buckets, first, 23, last);

        snapshot.Buckets.Should().HaveCount(24);
        snapshot.Buckets[0].EventCount.Should().Be(1);
        snapshot.Buckets[23].EventCount.Should().Be(24);
        snapshot.PeakHour.Should().Be(23);
        snapshot.FirstActionAt.Should().Be(first);
        snapshot.LastActionAt.Should().Be(last);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(100)]
    public void Constructor_InvalidPeakHour_Throws(int peakHour)
    {
        var buckets = Enumerable.Range(0, 24)
            .Select(h => new CadenceBucket(h, 0))
            .ToList()
            .AsReadOnly();

        var act = () => new CadenceSnapshot(buckets, null, peakHour, null);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("peakHour");
    }

    [Fact]
    public void Constructor_MidnightBoundary_HandlesHourZero()
    {
        var midnight = new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero);
        var buckets = Enumerable.Range(0, 24)
            .Select(h => new CadenceBucket(h, h == 0 ? 3 : 0))
            .ToList()
            .AsReadOnly();

        var snapshot = new CadenceSnapshot(buckets, midnight, 0, midnight);

        snapshot.Buckets[0].EventCount.Should().Be(3);
        snapshot.PeakHour.Should().Be(0);
        snapshot.FirstActionAt.Should().Be(midnight);
    }
}
