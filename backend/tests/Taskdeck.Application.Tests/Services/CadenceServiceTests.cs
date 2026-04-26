using System.Reflection;
using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CadenceServiceTests
{
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly CadenceService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public CadenceServiceTests()
    {
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _service = new CadenceService(_auditLogRepoMock.Object);
    }

    #region Validation

    [Fact]
    public async Task GetDailyCadenceAsync_EmptyUserId_ReturnsValidationError()
    {
        var result = await _service.GetDailyCadenceAsync(Guid.Empty, DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ValidationError");
    }

    #endregion

    #region Empty Day

    [Fact]
    public async Task GetDailyCadenceAsync_NoEntries_ReturnsEmptySnapshot()
    {
        SetupQueryReturns(new List<AuditLog>());

        var result = await _service.GetDailyCadenceAsync(_userId, DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value.Buckets.Should().HaveCount(24);
        result.Value.Buckets.Should().OnlyContain(b => b.EventCount == 0);
        result.Value.FirstActionAt.Should().BeNull();
        result.Value.PeakHour.Should().BeNull();
        result.Value.LastActionAt.Should().BeNull();
    }

    #endregion

    #region Single Event

    [Fact]
    public async Task GetDailyCadenceAsync_SingleEvent_BucketsCorrectly()
    {
        var timestamp = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var entries = new List<AuditLog> { CreateAuditLog(_userId, timestamp) };
        SetupQueryReturns(entries);

        var result = await _service.GetDailyCadenceAsync(_userId, timestamp);

        result.IsSuccess.Should().BeTrue();
        var snapshot = result.Value;

        snapshot.Buckets[14].EventCount.Should().Be(1);
        snapshot.Buckets.Where(b => b.Hour != 14).Should().OnlyContain(b => b.EventCount == 0);
        snapshot.FirstActionAt.Should().Be(timestamp);
        snapshot.LastActionAt.Should().Be(timestamp);
        snapshot.PeakHour.Should().Be(14);
    }

    #endregion

    #region Multiple Events

    [Fact]
    public async Task GetDailyCadenceAsync_MultipleEventsInSameHour_Aggregates()
    {
        var baseTime = new DateTimeOffset(2026, 4, 25, 10, 0, 0, TimeSpan.Zero);
        var entries = new List<AuditLog>
        {
            CreateAuditLog(_userId, baseTime),
            CreateAuditLog(_userId, baseTime.AddMinutes(15)),
            CreateAuditLog(_userId, baseTime.AddMinutes(45)),
        };
        SetupQueryReturns(entries);

        var result = await _service.GetDailyCadenceAsync(_userId, baseTime);

        result.IsSuccess.Should().BeTrue();
        result.Value.Buckets[10].EventCount.Should().Be(3);
        result.Value.PeakHour.Should().Be(10);
    }

    [Fact]
    public async Task GetDailyCadenceAsync_EventsAcrossHours_FindsPeakCorrectly()
    {
        var date = new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero);
        var entries = new List<AuditLog>
        {
            // Hour 8: 1 event
            CreateAuditLog(_userId, date.AddHours(8)),
            // Hour 14: 3 events (peak)
            CreateAuditLog(_userId, date.AddHours(14)),
            CreateAuditLog(_userId, date.AddHours(14).AddMinutes(10)),
            CreateAuditLog(_userId, date.AddHours(14).AddMinutes(20)),
            // Hour 20: 2 events
            CreateAuditLog(_userId, date.AddHours(20)),
            CreateAuditLog(_userId, date.AddHours(20).AddMinutes(30)),
        };
        SetupQueryReturns(entries);

        var result = await _service.GetDailyCadenceAsync(_userId, date);

        result.IsSuccess.Should().BeTrue();
        var snapshot = result.Value;

        snapshot.Buckets[8].EventCount.Should().Be(1);
        snapshot.Buckets[14].EventCount.Should().Be(3);
        snapshot.Buckets[20].EventCount.Should().Be(2);
        snapshot.PeakHour.Should().Be(14);
        snapshot.FirstActionAt.Should().Be(date.AddHours(8));
        snapshot.LastActionAt.Should().Be(date.AddHours(20).AddMinutes(30));
    }

    [Fact]
    public async Task GetDailyCadenceAsync_PeakTie_ReturnsEarliestHour()
    {
        var date = new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero);
        var entries = new List<AuditLog>
        {
            // Hour 5: 2 events
            CreateAuditLog(_userId, date.AddHours(5)),
            CreateAuditLog(_userId, date.AddHours(5).AddMinutes(30)),
            // Hour 18: 2 events (same count, but later)
            CreateAuditLog(_userId, date.AddHours(18)),
            CreateAuditLog(_userId, date.AddHours(18).AddMinutes(15)),
        };
        SetupQueryReturns(entries);

        var result = await _service.GetDailyCadenceAsync(_userId, date);

        result.IsSuccess.Should().BeTrue();
        // Ties go to earliest hour
        result.Value.PeakHour.Should().Be(5);
    }

    #endregion

    #region Midnight Boundary

    [Fact]
    public async Task GetDailyCadenceAsync_MidnightEvent_BucketsToHourZero()
    {
        var midnight = new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero);
        var entries = new List<AuditLog>
        {
            CreateAuditLog(_userId, midnight),
            CreateAuditLog(_userId, midnight.AddMinutes(1)),
        };
        SetupQueryReturns(entries);

        var result = await _service.GetDailyCadenceAsync(_userId, midnight);

        result.IsSuccess.Should().BeTrue();
        result.Value.Buckets[0].EventCount.Should().Be(2);
        result.Value.PeakHour.Should().Be(0);
    }

    [Fact]
    public async Task GetDailyCadenceAsync_EndOfDay_BucketsToHour23()
    {
        var lateNight = new DateTimeOffset(2026, 4, 25, 23, 59, 59, TimeSpan.Zero);
        var entries = new List<AuditLog>
        {
            CreateAuditLog(_userId, lateNight),
        };
        SetupQueryReturns(entries);

        var result = await _service.GetDailyCadenceAsync(_userId, lateNight);

        result.IsSuccess.Should().BeTrue();
        result.Value.Buckets[23].EventCount.Should().Be(1);
    }

    #endregion

    #region Date Normalization

    [Fact]
    public async Task GetDailyCadenceAsync_NonUtcDate_NormalizesToUtcDay()
    {
        // Pass a date with a non-UTC offset; service should normalize to UTC day
        var dateWithOffset = new DateTimeOffset(2026, 4, 25, 10, 0, 0, TimeSpan.FromHours(5));

        await _service.GetDailyCadenceAsync(_userId, dateWithOffset);

        // The query should use the UTC date boundaries (April 25 05:00 UTC = April 25 in UTC day)
        _auditLogRepoMock.Verify(r => r.QueryAsync(
            It.Is<DateTimeOffset>(d => d.UtcDateTime.Date == dateWithOffset.UtcDateTime.Date),
            It.IsAny<DateTimeOffset>(),
            _userId,
            null, null, null,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region All 24 Hours Filled

    [Fact]
    public async Task GetDailyCadenceAsync_AllHoursFilled_Returns24Buckets()
    {
        var date = new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero);
        var entries = Enumerable.Range(0, 24)
            .Select(h => CreateAuditLog(_userId, date.AddHours(h).AddMinutes(15)))
            .ToList();
        SetupQueryReturns(entries);

        var result = await _service.GetDailyCadenceAsync(_userId, date);

        result.IsSuccess.Should().BeTrue();
        result.Value.Buckets.Should().HaveCount(24);
        result.Value.Buckets.Should().OnlyContain(b => b.EventCount == 1);
        result.Value.FirstActionAt.Should().Be(date.AddMinutes(15));
        result.Value.LastActionAt.Should().Be(date.AddHours(23).AddMinutes(15));
    }

    #endregion

    #region Helpers

    private void SetupQueryReturns(List<AuditLog> entries)
    {
        _auditLogRepoMock
            .Setup(r => r.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
    }

    private static AuditLog CreateAuditLog(Guid userId, DateTimeOffset timestamp)
    {
        var entry = new AuditLog("Card", Guid.NewGuid(), AuditAction.Created, userId);

        // Use reflection to set the private Timestamp property for testing
        var timestampProperty = typeof(AuditLog).GetProperty(
            nameof(AuditLog.Timestamp),
            BindingFlags.Instance | BindingFlags.Public);
        timestampProperty!.SetValue(entry, timestamp);

        return entry;
    }

    #endregion
}
