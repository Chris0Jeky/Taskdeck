using System.Text;
using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class SourceAssetTests
{
    private const string Sha256OfAbc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void HashOf_ShouldBeLowerCaseSha256Hex()
    {
        SourceAsset.HashOf(Encoding.UTF8.GetBytes("abc")).Should().Be(Sha256OfAbc);
    }

    [Fact]
    public void FromInlineText_ShouldStoreTheTextVerbatimWithHashAndSize()
    {
        var captureId = Guid.NewGuid();
        const string text = "  line one\r\nline two é ";

        var asset = SourceAsset.FromInlineText(captureId, 0, text);

        asset.CaptureId.Should().Be(captureId);
        asset.Ordinal.Should().Be(0);
        asset.Modality.Should().Be(CaptureModality.Text);
        asset.StorageKind.Should().Be(SourceAssetStorageKind.InlineText);
        asset.MediaType.Should().Be(SourceAsset.PlainTextMediaType);
        asset.TextPayload!.Text.Should().Be(text, "a source is never normalised; derived normalised text is a representation");
        asset.ByteSize.Should().Be(Encoding.UTF8.GetByteCount(text));
        asset.ContentHash.Should().Be(SourceAsset.HashOf(Encoding.UTF8.GetBytes(text)));
        asset.BlobReferenceId.Should().BeNull();
        asset.LegacyArtefactId.Should().BeNull();
        asset.ExternalReference.Should().BeNull();
    }

    [Fact]
    public void FromInlineText_ShouldRejectBlankOrOversizedText()
    {
        var blank = () => SourceAsset.FromInlineText(Guid.NewGuid(), 0, "   ");
        var tooLong = () => SourceAsset.FromInlineText(Guid.NewGuid(), 0, new string('x', SourceAsset.MaxInlineTextLength + 1));

        blank.Should().Throw<DomainException>().WithMessage("*cannot be empty*");
        tooLong.Should().Throw<DomainException>().WithMessage($"*{SourceAsset.MaxInlineTextLength}*");
    }

    [Fact]
    public void MaxInlineTextLength_ShouldMatchTheShippedTranscriptCap()
    {
        SourceAsset.MaxInlineTextLength.Should().Be(Transcript.MaxTextLength,
            "no capture the legacy contract accepts may fail to mirror because its text is too long for an asset");
    }

    [Fact]
    public void FromExternalReference_ShouldTrimAndHashTheLocator()
    {
        var asset = SourceAsset.FromExternalReference(Guid.NewGuid(), 2, "  https://example.org/page  ", "Example page");

        asset.StorageKind.Should().Be(SourceAssetStorageKind.ExternalReference);
        asset.Modality.Should().Be(CaptureModality.Text);
        asset.MediaType.Should().Be(SourceAsset.UriListMediaType);
        asset.ExternalReference.Should().Be("https://example.org/page");
        asset.OriginalName.Should().Be("Example page");
        asset.ContentHash.Should().Be(SourceAsset.HashOf(Encoding.UTF8.GetBytes("https://example.org/page")));
        asset.TextPayload.Should().BeNull();

        var tooLong = () => SourceAsset.FromExternalReference(Guid.NewGuid(), 0, new string('u', SourceAsset.MaxExternalReferenceLength + 1));
        tooLong.Should().Throw<DomainException>().WithMessage($"*{SourceAsset.MaxExternalReferenceLength}*");
    }

    [Fact]
    public void FromBlobReference_ShouldCarryTheReferenceAndNormaliseTheHash()
    {
        var reference = Guid.NewGuid();

        var asset = SourceAsset.FromBlobReference(
            Guid.NewGuid(), 1, CaptureModality.Audio, "audio/webm", Sha256OfAbc.ToUpperInvariant(), 2_481_000, reference, "standup.webm");

        asset.StorageKind.Should().Be(SourceAssetStorageKind.Blob);
        asset.BlobReferenceId.Should().Be(reference);
        asset.ContentHash.Should().Be(Sha256OfAbc, "hashes are stored lower-case");
        asset.Modality.Should().Be(CaptureModality.Audio);
        asset.OriginalName.Should().Be("standup.webm");

        var emptyReference = () => SourceAsset.FromBlobReference(Guid.NewGuid(), 0, CaptureModality.Audio, "audio/webm", Sha256OfAbc, 10, Guid.Empty);
        var badHash = () => SourceAsset.FromBlobReference(Guid.NewGuid(), 0, CaptureModality.Audio, "audio/webm", "nope", 10, reference);
        var noBytes = () => SourceAsset.FromBlobReference(Guid.NewGuid(), 0, CaptureModality.Audio, "audio/webm", Sha256OfAbc, 0, reference);

        emptyReference.Should().Throw<DomainException>().WithMessage("*Blob reference ID*");
        badHash.Should().Throw<DomainException>().WithMessage("*SHA-256*");
        noBytes.Should().Throw<DomainException>().WithMessage("*byte size*");
    }

    [Fact]
    public void FromLegacyArtefact_ShouldPointAtTheArtefactWithoutCopying()
    {
        var artefactId = Guid.NewGuid();

        var asset = SourceAsset.FromLegacyArtefact(
            Guid.NewGuid(), 0, CaptureModality.Document, "application/pdf", Sha256OfAbc, 4096, artefactId, "brief.pdf");

        asset.StorageKind.Should().Be(SourceAssetStorageKind.LegacyArtefact);
        asset.LegacyArtefactId.Should().Be(artefactId);
        asset.BlobReferenceId.Should().BeNull();
        asset.Modality.Should().Be(CaptureModality.Document);

        var empty = () => SourceAsset.FromLegacyArtefact(Guid.NewGuid(), 0, CaptureModality.Document, "application/pdf", Sha256OfAbc, 4096, Guid.Empty);
        empty.Should().Throw<DomainException>().WithMessage("*Legacy artefact ID*");
    }

    [Fact]
    public void Constructor_ShouldRejectInvalidMetadata()
    {
        var emptyCapture = () => SourceAsset.FromInlineText(Guid.Empty, 0, "text");
        var negativeOrdinal = () => SourceAsset.FromInlineText(Guid.NewGuid(), -1, "text");
        var blankMediaType = () => SourceAsset.FromInlineText(Guid.NewGuid(), 0, "text", " ");
        var longMediaType = () => SourceAsset.FromInlineText(Guid.NewGuid(), 0, "text", new string('m', SourceAsset.MaxMediaTypeLength + 1));
        var longName = () => SourceAsset.FromInlineText(Guid.NewGuid(), 0, "text", originalName: new string('n', SourceAsset.MaxOriginalNameLength + 1));
        var undefinedModality = () => SourceAsset.FromBlobReference(Guid.NewGuid(), 0, (CaptureModality)9, "audio/webm", Sha256OfAbc, 1, Guid.NewGuid());

        emptyCapture.Should().Throw<DomainException>().WithMessage("*Capture ID*");
        negativeOrdinal.Should().Throw<DomainException>().WithMessage("*ordinal*");
        blankMediaType.Should().Throw<DomainException>().WithMessage("*Media type*");
        longMediaType.Should().Throw<DomainException>().WithMessage("*Media type*");
        longName.Should().Throw<DomainException>().WithMessage("*Original name*");
        undefinedModality.Should().Throw<DomainException>().WithMessage("*modality*");
    }
}
