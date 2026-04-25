using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class StreakServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly StreakService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public StreakServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);

        _auditLogRepoMock
            .Setup(a => a.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog>());

        _service = new StreakService(_unitOfWorkMock.Object);
    }

    #region Validation

    [Fact]
    public async Task GetStreakAsync_ShouldFail_WhenUserIdIsEmpty()
    {
        var result = await _service.GetStreakAsync(Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("User ID");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    [InlineData(1000)]
    public async Task GetStreakAsync_ShouldFail_WhenDayCountOutOfRange(int dayCount)
    {
        var result = await _service.GetStreakAsync(_userId, dayCount);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Day count");
    }

    #endregion

    #region Empty History

    [Fact]
    public async Task GetStreakAsync_EmptyHistory_ShouldReturnAllZeroBuckets()
    {
        var result = await _service.GetStreakAsync(_userId, 7);

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().HaveCount(7);
        result.Value.Days.Should().OnlyContain(d => d.IntensityBucket == 0);
        result.Value.CurrentStreakLength.Should().Be(0);
        result.Value.LongestStreakLength.Should().Be(0);
    }

    #endregion

    #region Single Day

    [Fact]
    public async Task GetStreakAsync_SingleDayActivity_ShouldHaveCurrentStreakOfOne()
    {
        var today = DateTimeOffset.UtcNow;
        var auditLog = CreateAuditLog(_userId, today);

        _auditLogRepoMock
            .Setup(a => a.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.Is<Guid?>(id => id == _userId),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog> { auditLog });

        var result = await _service.GetStreakAsync(_userId, 7);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentStreakLength.Should().Be(1);
        result.Value.LongestStreakLength.Should().Be(1);

        var todayDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayDay = result.Value.Days.SingleOrDefault(d => d.Date == todayDate);
        todayDay.Should().NotBeNull();
        todayDay!.IntensityBucket.Should().BeGreaterThan(0);
    }

    #endregion

    #region Continuous Streak

    [Fact]
    public async Task GetStreakAsync_ContinuousStreak_ShouldComputeCorrectly()
    {
        var today = DateTimeOffset.UtcNow;
        var audits = new List<AuditLog>();

        // 5 consecutive days of activity (today and 4 days before)
        for (int i = 0; i < 5; i++)
        {
            audits.Add(CreateAuditLog(_userId, today.AddDays(-i)));
        }

        _auditLogRepoMock
            .Setup(a => a.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.Is<Guid?>(id => id == _userId),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);

        var result = await _service.GetStreakAsync(_userId, 10);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentStreakLength.Should().Be(5);
        result.Value.LongestStreakLength.Should().Be(5);
    }

    #endregion

    #region Gap in Streak

    [Fact]
    public async Task GetStreakAsync_GapInStreak_ShouldBreakCurrentStreak()
    {
        var today = DateTimeOffset.UtcNow;
        var audits = new List<AuditLog>
        {
            // Today and yesterday: current streak = 2
            CreateAuditLog(_userId, today),
            CreateAuditLog(_userId, today.AddDays(-1)),
            // Gap on day -2
            // 3 days of activity before the gap: day -3, -4, -5
            CreateAuditLog(_userId, today.AddDays(-3)),
            CreateAuditLog(_userId, today.AddDays(-4)),
            CreateAuditLog(_userId, today.AddDays(-5)),
        };

        _auditLogRepoMock
            .Setup(a => a.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.Is<Guid?>(id => id == _userId),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);

        var result = await _service.GetStreakAsync(_userId, 10);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentStreakLength.Should().Be(2);
        result.Value.LongestStreakLength.Should().Be(3);
    }

    [Fact]
    public async Task GetStreakAsync_GapAtEnd_ShouldHaveZeroCurrentStreak()
    {
        var today = DateTimeOffset.UtcNow;
        var audits = new List<AuditLog>
        {
            // Activity only 3 days ago (no activity today, yesterday, or day before)
            CreateAuditLog(_userId, today.AddDays(-3)),
            CreateAuditLog(_userId, today.AddDays(-4)),
        };

        _auditLogRepoMock
            .Setup(a => a.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.Is<Guid?>(id => id == _userId),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);

        var result = await _service.GetStreakAsync(_userId, 10);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentStreakLength.Should().Be(0);
        result.Value.LongestStreakLength.Should().Be(2);
    }

    #endregion

    #region Intensity Bucketing

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 4, 1)]
    [InlineData(2, 4, 2)]
    [InlineData(3, 4, 3)]
    [InlineData(4, 4, 4)]
    [InlineData(1, 1, 4)] // Only one entry => max => bucket 4
    [InlineData(0, 0, 0)] // maxCount 0 => bucket 0
    public void ComputeIntensityBucket_ShouldReturnCorrectBucket(
        int count, int maxCount, int expectedBucket)
    {
        var bucket = StreakService.ComputeIntensityBucket(count, maxCount);
        bucket.Should().Be(expectedBucket);
    }

    [Fact]
    public void ComputeIntensityBucket_BoundaryValues_ShouldBeConsistent()
    {
        // At exactly 25%, 50%, 75% boundaries
        StreakService.ComputeIntensityBucket(25, 100).Should().Be(1);
        StreakService.ComputeIntensityBucket(50, 100).Should().Be(2);
        StreakService.ComputeIntensityBucket(75, 100).Should().Be(3);
        StreakService.ComputeIntensityBucket(100, 100).Should().Be(4);

        // Just above boundaries
        StreakService.ComputeIntensityBucket(26, 100).Should().Be(2);
        StreakService.ComputeIntensityBucket(51, 100).Should().Be(3);
        StreakService.ComputeIntensityBucket(76, 100).Should().Be(4);
    }

    [Fact]
    public async Task GetStreakAsync_VaryingActivity_ShouldProduceCorrectBuckets()
    {
        var today = DateTimeOffset.UtcNow;
        var audits = new List<AuditLog>();

        // Day 0 (today): 4 entries
        for (int i = 0; i < 4; i++)
            audits.Add(CreateAuditLog(_userId, today));

        // Day -1: 1 entry
        audits.Add(CreateAuditLog(_userId, today.AddDays(-1)));

        // Day -2: 2 entries
        for (int i = 0; i < 2; i++)
            audits.Add(CreateAuditLog(_userId, today.AddDays(-2)));

        _auditLogRepoMock
            .Setup(a => a.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.Is<Guid?>(id => id == _userId),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);

        var result = await _service.GetStreakAsync(_userId, 5);

        result.IsSuccess.Should().BeTrue();

        var todayDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayDay = result.Value.Days.Single(d => d.Date == todayDate);
        todayDay.IntensityBucket.Should().Be(4); // max = 4, count = 4 => ratio 1.0 => bucket 4

        var yesterdayDay = result.Value.Days.Single(d => d.Date == todayDate.AddDays(-1));
        yesterdayDay.IntensityBucket.Should().Be(1); // count = 1, max = 4 => ratio 0.25 => bucket 1

        var twoDaysAgoDay = result.Value.Days.Single(d => d.Date == todayDate.AddDays(-2));
        twoDaysAgoDay.IntensityBucket.Should().Be(2); // count = 2, max = 4 => ratio 0.5 => bucket 2
    }

    #endregion

    #region ComputeDays Unit Tests

    [Fact]
    public void ComputeDays_ShouldIncludeEveryDateInRange()
    {
        var start = new DateOnly(2026, 4, 1);
        var end = new DateOnly(2026, 4, 5);
        var counts = new Dictionary<DateOnly, int>();

        var days = StreakService.ComputeDays(start, end, counts);

        days.Should().HaveCount(5);
        days[0].Date.Should().Be(new DateOnly(2026, 4, 1));
        days[4].Date.Should().Be(new DateOnly(2026, 4, 5));
    }

    [Fact]
    public void ComputeDays_SingleDay_ShouldReturnOne()
    {
        var date = new DateOnly(2026, 4, 20);
        var counts = new Dictionary<DateOnly, int> { { date, 5 } };

        var days = StreakService.ComputeDays(date, date, counts);

        days.Should().HaveCount(1);
        days[0].Date.Should().Be(date);
        days[0].IntensityBucket.Should().Be(4); // count = max = 5 => ratio 1.0 => bucket 4
    }

    [Fact]
    public void ComputeDays_ShouldDefaultSealedToFalse()
    {
        var start = new DateOnly(2026, 4, 1);
        var end = new DateOnly(2026, 4, 3);

        var days = StreakService.ComputeDays(start, end, new Dictionary<DateOnly, int>());

        days.Should().OnlyContain(d => d.IsSealed == false);
    }

    #endregion

    #region ComputeCurrentStreak Unit Tests

    [Fact]
    public void ComputeCurrentStreak_Empty_ShouldReturnZero()
    {
        StreakService.ComputeCurrentStreak(Array.Empty<StreakDay>()).Should().Be(0);
    }

    [Fact]
    public void ComputeCurrentStreak_AllActive_ShouldReturnTotal()
    {
        var days = Enumerable.Range(0, 5)
            .Select(i => new StreakDay(new DateOnly(2026, 4, 1).AddDays(i), false, 2))
            .ToList();

        StreakService.ComputeCurrentStreak(days).Should().Be(5);
    }

    [Fact]
    public void ComputeCurrentStreak_LastDayInactive_ShouldReturnZero()
    {
        var days = new List<StreakDay>
        {
            new(new DateOnly(2026, 4, 1), false, 3),
            new(new DateOnly(2026, 4, 2), false, 2),
            new(new DateOnly(2026, 4, 3), false, 0), // last day inactive
        };

        StreakService.ComputeCurrentStreak(days).Should().Be(0);
    }

    [Fact]
    public void ComputeCurrentStreak_GapThenActive_ShouldCountOnlyTrailingActive()
    {
        var days = new List<StreakDay>
        {
            new(new DateOnly(2026, 4, 1), false, 3),
            new(new DateOnly(2026, 4, 2), false, 0), // gap
            new(new DateOnly(2026, 4, 3), false, 1),
            new(new DateOnly(2026, 4, 4), false, 2),
        };

        StreakService.ComputeCurrentStreak(days).Should().Be(2);
    }

    #endregion

    #region ComputeLongestStreak Unit Tests

    [Fact]
    public void ComputeLongestStreak_Empty_ShouldReturnZero()
    {
        StreakService.ComputeLongestStreak(Array.Empty<StreakDay>()).Should().Be(0);
    }

    [Fact]
    public void ComputeLongestStreak_AllActive_ShouldReturnTotal()
    {
        var days = Enumerable.Range(0, 7)
            .Select(i => new StreakDay(new DateOnly(2026, 4, 1).AddDays(i), false, 1))
            .ToList();

        StreakService.ComputeLongestStreak(days).Should().Be(7);
    }

    [Fact]
    public void ComputeLongestStreak_AllInactive_ShouldReturnZero()
    {
        var days = Enumerable.Range(0, 5)
            .Select(i => new StreakDay(new DateOnly(2026, 4, 1).AddDays(i), false, 0))
            .ToList();

        StreakService.ComputeLongestStreak(days).Should().Be(0);
    }

    [Fact]
    public void ComputeLongestStreak_MultipleRuns_ShouldReturnLongest()
    {
        var days = new List<StreakDay>
        {
            new(new DateOnly(2026, 4, 1), false, 1),
            new(new DateOnly(2026, 4, 2), false, 2),
            new(new DateOnly(2026, 4, 3), false, 0), // gap
            new(new DateOnly(2026, 4, 4), false, 3),
            new(new DateOnly(2026, 4, 5), false, 4),
            new(new DateOnly(2026, 4, 6), false, 1),
            new(new DateOnly(2026, 4, 7), false, 0), // gap
            new(new DateOnly(2026, 4, 8), false, 2),
        };

        StreakService.ComputeLongestStreak(days).Should().Be(3); // days 4-5-6
    }

    [Fact]
    public void ComputeLongestStreak_SingleActiveDay_ShouldReturnOne()
    {
        var days = new List<StreakDay>
        {
            new(new DateOnly(2026, 4, 1), false, 0),
            new(new DateOnly(2026, 4, 2), false, 1),
            new(new DateOnly(2026, 4, 3), false, 0),
        };

        StreakService.ComputeLongestStreak(days).Should().Be(1);
    }

    #endregion

    #region Day Count Boundary Tests

    [Fact]
    public async Task GetStreakAsync_MinDayCount_ShouldSucceed()
    {
        var result = await _service.GetStreakAsync(_userId, 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetStreakAsync_MaxDayCount_ShouldSucceed()
    {
        var result = await _service.GetStreakAsync(_userId, 365);

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().HaveCount(365);
    }

    [Fact]
    public async Task GetStreakAsync_DefaultDayCount_ShouldBe90()
    {
        var result = await _service.GetStreakAsync(_userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().HaveCount(90);
    }

    #endregion

    #region Helpers

    private static AuditLog CreateAuditLog(Guid userId, DateTimeOffset timestamp)
    {
        var auditLog = new AuditLog(
            entityType: "card",
            entityId: Guid.NewGuid(),
            action: AuditAction.Created,
            userId: userId,
            changes: "test audit entry");

        // Use reflection to set the Timestamp since it's set in constructor to UtcNow
        var timestampProperty = typeof(AuditLog).GetProperty("Timestamp")!;
        timestampProperty.SetValue(auditLog, timestamp);

        return auditLog;
    }

    #endregion
}
