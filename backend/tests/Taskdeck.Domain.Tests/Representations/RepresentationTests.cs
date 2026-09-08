using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Representations;

public sealed class RepresentationTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid CaptureId = Guid.NewGuid();
    private static readonly Guid AssetId = Guid.NewGuid();

    private static Representation Header(
        Guid? id = null, Guid? capture = null, Guid? owner = null,
        RepresentationKind kind = RepresentationKind.Transcript,
        Guid? parentAsset = null, Guid? parentRepresentation = null,
        RepresentationQualityState quality = RepresentationQualityState.Final,
        IEnumerable<string>? warnings = null, bool legacy = false, string? hash = null) =>
        new(id ?? Guid.NewGuid(), legacy ? null : capture ?? CaptureId, owner ?? Owner, kind,
            parentRepresentation.HasValue ? parentAsset : parentAsset ?? AssetId,
            parentRepresentation, null, "legacy.transcript", "1", null,
            new string('a', 64), 1, hash ?? new string('B', 64), null, quality,
            warnings ?? [], DateTimeOffset.UnixEpoch, legacy);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Constructor_RejectsMissingOrAmbiguousParent(bool asset, bool representation)
    {
        Action act = () => new Representation(Guid.NewGuid(), CaptureId, Owner,
            RepresentationKind.Transcript, asset ? AssetId : null,
            representation ? Guid.NewGuid() : null, null, "legacy.transcript", "1", null,
            new string('a', 64), 1, new string('b', 64), null,
            RepresentationQualityState.Final, [], DateTimeOffset.UnixEpoch);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_PreservesIdentityAndDefensivelyCopiesWarnings()
    {
        var id = Guid.NewGuid();
        var warnings = new List<string> { " partial \r\n" };
        var row = Header(id: id, quality: RepresentationQualityState.Provisional, warnings: warnings);
        warnings[0] = "changed";
        row.Id.Should().Be(id);
        row.ContentHash.Should().Be(new string('b', 64));
        row.Warnings.Should().Equal(" partial \r\n");
        Action mutate = () => ((IList<string>)row.Warnings)[0] = "changed";
        mutate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Constructor_AllowsNullCaptureOnlyWhenExplicitlyMigrating()
    {
        Header(legacy: true).CaptureId.Should().BeNull();
        Action act = () => new Representation(Guid.NewGuid(), null, Owner, RepresentationKind.Transcript,
            AssetId, null, null, "legacy.transcript", "1", null, new string('a', 64), 1,
            new string('b', 64), null, RepresentationQualityState.Final, [], DateTimeOffset.UnixEpoch);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_RejectsEmptyOwnerSelfParentAndProjectedQuality()
    {
        Action emptyOwner = () => Header(owner: Guid.Empty);
        var id = Guid.NewGuid();
        Action self = () => Header(id: id, parentRepresentation: id);
        Action projected = () => Header(quality: RepresentationQualityState.Superseded);
        emptyOwner.Should().Throw<DomainException>();
        self.Should().Throw<DomainException>();
        projected.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_RejectsEmptyIdentifiersAndUnknownKind()
    {
        foreach (Action act in new Action[]
        {
            () => Header(id: Guid.Empty),
            () => Header(capture: Guid.Empty),
            () => Header(parentAsset: Guid.Empty),
            () => Header(parentRepresentation: Guid.Empty),
            () => Header(kind: (RepresentationKind)99),
            () => Header(quality: (RepresentationQualityState)99)
        })
            act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Constructor_RejectsMalformedHash(string hash)
    {
        Action act = () => Header(hash: hash);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("processor")]
    [InlineData("version")]
    [InlineData("configuration")]
    [InlineData("schema")]
    [InlineData("run")]
    [InlineData("created")]
    public void Constructor_RejectsIncompleteProvenance(string invalidField)
    {
        Action act = () => new Representation(Guid.NewGuid(), CaptureId, Owner,
            RepresentationKind.Transcript, AssetId, null, invalidField == "run" ? Guid.Empty : null,
            invalidField == "processor" ? " " : "legacy.transcript",
            invalidField == "version" ? "" : "1", null,
            invalidField == "configuration" ? "unknown" : new string('a', 64),
            invalidField == "schema" ? 0 : 1, new string('b', 64), null,
            RepresentationQualityState.Final, [], invalidField == "created" ? default : DateTimeOffset.UnixEpoch);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ParentValidation_RequiresMatchingOwnerCaptureAndEndpoint()
    {
        var parent = Header();
        Header(parentRepresentation: parent.Id).ValidateParent(parent);
        Action wrongOwner = () => Header(owner: Guid.NewGuid(), parentRepresentation: parent.Id).ValidateParent(parent);
        Action wrongCapture = () => Header(capture: Guid.NewGuid(), parentRepresentation: parent.Id).ValidateParent(parent);
        Action wrongEndpoint = () => Header(parentRepresentation: Guid.NewGuid()).ValidateParent(parent);
        Action unresolved = () => Header(parentRepresentation: parent.Id, legacy: true).ValidateParent(parent);
        wrongOwner.Should().Throw<DomainException>();
        wrongCapture.Should().Throw<DomainException>();
        wrongEndpoint.Should().Throw<DomainException>();
        unresolved.Should().Throw<DomainException>();
    }

    [Fact]
    public void AssetParentValidation_UsesCaptureOwnership()
    {
        var capture = Capture.FromQueueRequest(CaptureId, Owner, CaptureSource.Typed, null, null, null);
        var asset = SourceAsset.FromInlineText(CaptureId, 0, "text");
        Header(parentAsset: asset.Id).ValidateParent(asset, capture);
        Action wrongOwner = () => Header(owner: Guid.NewGuid(), parentAsset: asset.Id).ValidateParent(asset, capture);
        Action wrongCapture = () => Header(capture: Guid.NewGuid(), parentAsset: asset.Id).ValidateParent(asset, capture);
        wrongOwner.Should().Throw<DomainException>();
        wrongCapture.Should().Throw<DomainException>();
    }

    [Fact]
    public void Supersession_LeavesOldRowAndQualityUntouchedAndAllowsEqualTimestamps()
    {
        var old = Header(quality: RepresentationQualityState.Provisional, warnings: ["partial"]);
        var replacement = Header();
        var edge = new RepresentationSupersession(old, replacement);
        edge.RepresentationId.Should().Be(old.Id);
        edge.SupersededByRepresentationId.Should().Be(replacement.Id);
        old.QualityState.Should().Be(RepresentationQualityState.Provisional);
        old.Warnings.Should().Equal("partial");
        replacement.QualityState.Should().Be(RepresentationQualityState.Final);
    }

    [Fact]
    public void Supersession_RejectsSelfCrossOwnerCaptureKindAndUnresolvedCapture()
    {
        var old = Header();
        foreach (var replacement in new[] { old, Header(owner: Guid.NewGuid()),
            Header(capture: Guid.NewGuid()), Header(kind: RepresentationKind.OcrText), Header(legacy: true) })
        {
            Action act = () => new RepresentationSupersession(old, replacement);
            act.Should().Throw<DomainException>();
        }
    }

    [Fact]
    public void TypedPayloadValidation_RequiresKindPreservedIdAndTranscriptOwner()
    {
        var transcript = new Transcript(Owner, CaptureSource.Typed, "text");
        Header(id: transcript.Id).ValidatePayload(transcript);
        var extraction = new ArtefactExtraction(Guid.NewGuid(), "test", "1", [], "text");
        Header(id: extraction.Id, kind: RepresentationKind.NormalizedText).ValidatePayload(extraction);
        Action wrongKind = () => Header(id: transcript.Id, kind: RepresentationKind.OcrText).ValidatePayload(transcript);
        Action wrongId = () => Header().ValidatePayload(transcript);
        Action wrongOwner = () => Header(id: transcript.Id, owner: Guid.NewGuid()).ValidatePayload(transcript);
        Action wrongExtractionKind = () => Header(id: extraction.Id).ValidatePayload(extraction);
        wrongKind.Should().Throw<DomainException>();
        wrongId.Should().Throw<DomainException>();
        wrongOwner.Should().Throw<DomainException>();
        wrongExtractionKind.Should().Throw<DomainException>();
    }

    [Fact]
    public void TextHash_UsesKnownSha256AndNormalizesOnlyLineEndings()
    {
        Representation.ComputeTextContentHash("abc").Should().Be(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        Representation.ComputeTextContentHash("a\r\nb\rc").Should().Be(Representation.ComputeTextContentHash("a\nb\nc"));
        Representation.ComputeTextContentHash("a\n").Should().NotBe(Representation.ComputeTextContentHash("a"));
        Representation.ComputeTextContentHash(" a").Should().NotBe(Representation.ComputeTextContentHash("a"));
        Representation.ComputeTextContentHash("\u00e9").Should().NotBe(Representation.ComputeTextContentHash("e\u0301"));
        Action malformed = () => Representation.ComputeTextContentHash("\ud800");
        malformed.Should().Throw<DomainException>();
    }
}
