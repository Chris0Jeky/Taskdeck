using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class SourceArtefactTests
{
    [Fact]
    public void Constructor_ShouldCreateImmutableMetadata()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();

        var artefact = new SourceArtefact(
            userId,
            ArtefactKind.Image,
            "image/png",
            "evidence.png",
            42,
            new string('A', 64),
            CaptureSource.Import,
            boardId,
            "local-import",
            captureId);

        artefact.UserId.Should().Be(userId);
        artefact.BoardId.Should().Be(boardId);
        artefact.CreatedFromCaptureId.Should().Be(captureId);
        artefact.Sha256.Should().Be(new string('a', 64));
        artefact.OriginReference.Should().Be("local-import");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-digest")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Constructor_ShouldRejectInvalidSha256(string sha256)
    {
        var act = () => new SourceArtefact(
            Guid.NewGuid(),
            ArtefactKind.Pdf,
            "application/pdf",
            "notes.pdf",
            1,
            sha256,
            CaptureSource.Import);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ArtefactBlob_ShouldDefensivelyCopyInput()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var blob = new ArtefactBlob(Guid.NewGuid(), bytes);

        bytes[0] = 9;

        blob.Content.Should().Equal(1, 2, 3);
    }
}
