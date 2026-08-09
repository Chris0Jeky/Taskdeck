using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class TranscriptTests
{
    [Fact]
    public void Constructor_NormalizesCrLfAndRetainsUnicodeLineSegments()
    {
        var transcript = new Transcript(
            Guid.NewGuid(),
            CaptureSource.TranscriptPaste,
            "Jéssica: café\r\nMina: ✅ done\rfinal line",
            [
                new TranscriptSegment(0, 0, "Jéssica", 0),
                new TranscriptSegment(1, 2, "Mina", 1_250)
            ]);

        transcript.Text.Should().Be("Jéssica: café\nMina: ✅ done\nfinal line");
        transcript.Text.Should().NotContain("\r");
        transcript.Segments.Should().ContainInOrder(
            new TranscriptSegment(0, 0, "Jéssica", 0),
            new TranscriptSegment(1, 2, "Mina", 1_250));
    }

    [Fact]
    public void Constructor_RejectsSegmentOutsideNormalizedLineRange()
    {
        var act = () => new Transcript(
            Guid.NewGuid(),
            CaptureSource.TranscriptFile,
            "only line",
            [new TranscriptSegment(0, 1)]);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\n")]
    public void Constructor_RejectsEmptyNormalizedText(string text)
    {
        var act = () => new Transcript(Guid.NewGuid(), CaptureSource.Paste, text);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_RejectsEmptyIdentityAndInvalidCaptureSource()
    {
        var emptyUser = () => new Transcript(Guid.Empty, CaptureSource.Paste, "text");
        var invalidSource = () => new Transcript(Guid.NewGuid(), (CaptureSource)999, "text");

        emptyUser.Should().Throw<DomainException>();
        invalidSource.Should().Throw<DomainException>();
    }
}
