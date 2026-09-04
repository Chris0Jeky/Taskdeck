using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// #2193: the day a triage run resolves partial dates against is the CAPTURE's day, and it is
/// derived from the server stamp — a client can move the offset, never the instant.
/// </summary>
public class CaptureTriageAnchorTests
{
    [Fact]
    public void FromCapture_ShouldUseTheUtcDayOfTheServerStamp_WhenTheCaptureReportsNoOffset()
    {
        var anchor = CaptureTriageAnchor.FromCapture(
            new DateTimeOffset(2026, 8, 29, 21, 30, 0, TimeSpan.Zero));

        anchor.ReferenceDate.Should().Be(new DateOnly(2026, 8, 29));
        anchor.CaptureLocalOffset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void FromCapture_ShouldUseTheCaptureLocalDay_WhenTheCaptureReportsAWesternOffset()
    {
        // Server stamps 02:00 UTC on 1 September; the speaker in UTC-07:00 is still on 31 August,
        // and "1 September" said in that meeting means the NEXT day, not the current one.
        var anchor = CaptureTriageAnchor.FromCapture(
            new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 19, 0, 0, TimeSpan.FromHours(-7)));

        anchor.ReferenceDate.Should().Be(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void FromCapture_ShouldCrossTheYearBoundary_WhenTheCaptureLocalDayIsInThePreviousYear()
    {
        var anchor = CaptureTriageAnchor.FromCapture(
            new DateTimeOffset(2027, 1, 1, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.FromHours(-5)));

        anchor.ReferenceDate.Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void FromCapture_ShouldKeepTheServerInstant_WhenTheClientTimestampDisagreesWithIt()
    {
        // Only the OFFSET is taken from the client. A client claiming a 2019 creation date cannot
        // drag the reference date with it.
        var anchor = CaptureTriageAnchor.FromCapture(
            new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero));

        anchor.CapturedAtServer.Should().Be(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        anchor.ReferenceDate.Should().Be(new DateOnly(2026, 8, 29));
    }

    [Fact]
    public void FromCapture_ShouldKeepTheWidestRealOffset()
    {
        // UTC+14 (Kiritimati) is the eastern extreme of the real offset range, whose western
        // extreme is UTC-12; the anchor accepts the whole range and only discards a value outside
        // it. DateTimeOffset itself already bounds construction to +/-14h, so an out-of-range
        // value could only arrive through some future construction path - the clamp is a guard,
        // not a reachable branch today.
        var anchor = CaptureTriageAnchor.FromCapture(
            new DateTimeOffset(2026, 8, 29, 23, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 30, 13, 0, 0, TimeSpan.FromHours(14)));

        anchor.CaptureLocalOffset.Should().Be(TimeSpan.FromHours(14));
        anchor.ReferenceDate.Should().Be(new DateOnly(2026, 8, 30));
    }

    [Fact]
    public void FromCapture_ShouldNotDriftWithTheTriageDay_WhenTriageRunsDaysLater()
    {
        // Delayed triage (a backlogged queue or a retry days after capture): the anchor is a
        // property of the capture row, so it cannot move with the run.
        var capturedAt = DateTimeOffset.UtcNow.AddDays(-11);
        var anchor = CaptureTriageAnchor.FromCapture(capturedAt);

        anchor.ReferenceDate.Should().Be(DateOnly.FromDateTime(capturedAt.UtcDateTime));
        anchor.ReferenceDate.Should().NotBe(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public void ForImmediateTriage_ShouldCarryTheCapturesReportedOffset()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.TranscriptPaste,
            "Alice: let's meet on 1 September.",
            ClientCreatedAt: new DateTimeOffset(2026, 8, 31, 19, 0, 0, TimeSpan.FromHours(-7)));

        var anchor = CaptureTriageAnchor.ForImmediateTriage(payload);

        anchor.CaptureLocalOffset.Should().Be(TimeSpan.FromHours(-7));
    }
}
