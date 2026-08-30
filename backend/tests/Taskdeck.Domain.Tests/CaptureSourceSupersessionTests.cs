using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests;

/// <summary>
/// CF-01 (#2255), late review finding on #2320: sources are immutable, so a post-intake edit must
/// produce a SUPERSEDING asset and never rewrite one in place. What the user first gave Taskdeck
/// stays readable, and the current text is the newest asset nothing has superseded.
/// </summary>
public sealed class CaptureSourceSupersessionTests
{
    private static Capture NewCapture(CaptureSource source = CaptureSource.Typed) =>
        Capture.FromQueueRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            source,
            contextBoardId: null,
            capturedAtClient: null,
            userTitle: null);

    [Fact]
    public void SupersedeInlineTextSource_ShouldAppendAReplacementAndKeepTheOriginalReadable()
    {
        var capture = NewCapture();
        var original = capture.AddInlineTextSource("call Dana about the venue");

        var replacement = capture.SupersedeInlineTextSource("call Dana about the venue on Friday");

        capture.SourceAssets.Should().HaveCount(2, "the intake record survives the correction");
        original.SupersededByAssetId.Should().Be(replacement.Id);
        original.IsActive.Should().BeFalse();
        original.TextPayload!.Text.Should().Be("call Dana about the venue", "the stored bytes are never rewritten");
        replacement.SupersedesAssetId.Should().Be(original.Id);
        replacement.IsActive.Should().BeTrue();
        replacement.Ordinal.Should().Be(1);
        capture.ActiveSourceAssets.Should().ContainSingle().Which.Should().Be(replacement);
        capture.CurrentText.Should().Be("call Dana about the venue on Friday");
    }

    [Fact]
    public void SupersedeInlineTextSource_ShouldChainAcrossRepeatedEdits()
    {
        var capture = NewCapture();
        capture.AddInlineTextSource("first");
        var second = capture.SupersedeInlineTextSource("second");
        var third = capture.SupersedeInlineTextSource("third");

        third.SupersedesAssetId.Should().Be(second.Id);
        second.SupersededByAssetId.Should().Be(third.Id);
        capture.SourceAssets.Should().HaveCount(3);
        capture.ActiveSourceAssets.Should().ContainSingle().Which.Id.Should().Be(third.Id);
        capture.CurrentText.Should().Be("third");
    }

    [Fact]
    public void SupersedeInlineTextSource_OnACaptureWithoutTextShouldSimplyStoreTheFirstOne()
    {
        var capture = NewCapture();

        var asset = capture.SupersedeInlineTextSource("typed after the fact");

        asset.SupersedesAssetId.Should().BeNull();
        capture.CurrentText.Should().Be("typed after the fact");
    }

    [Fact]
    public void SupersedeInlineTextSource_ShouldRejectAnEmptyCorrectionWithoutLosingTheCurrentSource()
    {
        var capture = NewCapture();
        var original = capture.AddInlineTextSource("keep me");

        var act = () => capture.SupersedeInlineTextSource("   ");

        act.Should().Throw<DomainException>();
        capture.SourceAssets.Should().ContainSingle();
        original.IsActive.Should().BeTrue("a rejected correction never leaves the capture without a source");
        capture.CurrentText.Should().Be("keep me");
    }

    [Fact]
    public void SupersedeInlineTextSource_ShouldRejectEditingAnArchivedCapture()
    {
        var capture = NewCapture();
        capture.AddInlineTextSource("original");
        capture.Archive();

        var act = () => capture.SupersedeInlineTextSource("edited");

        act.Should().Throw<DomainException>().WithMessage("*archived*");
    }

    [Fact]
    public void SourceAssetCap_ShouldCountOnlyActiveAssetsSoEditingIsNeverRejected()
    {
        var capture = NewCapture();
        capture.AddInlineTextSource("original");

        // Far more corrections than the 32-asset cap: a capture the shipped contract lets a user
        // edit must never start failing because its correction history grew.
        for (var edit = 0; edit < Capture.MaxSourceAssets + 5; edit++)
        {
            capture.SupersedeInlineTextSource($"edit {edit}");
        }

        capture.SourceAssets.Should().HaveCount(Capture.MaxSourceAssets + 6);
        capture.ActiveSourceAssets.Should().ContainSingle();
        capture.CurrentText.Should().Be($"edit {Capture.MaxSourceAssets + 4}");
    }

    [Fact]
    public void SourceAssetCap_ShouldStillBoundHowManyInputsACaptureHas()
    {
        var capture = NewCapture();
        for (var index = 0; index < Capture.MaxSourceAssets; index++)
        {
            capture.AddInlineTextSource($"input {index}");
        }

        var act = () => capture.AddInlineTextSource("one too many");

        act.Should().Throw<DomainException>().WithMessage("*active source assets*");
    }

    [Fact]
    public void AddExternalReferenceSource_ShouldStoreTheLocatorVerbatimWithoutFetchingIt()
    {
        var capture = NewCapture(CaptureSource.WebClip);
        capture.AddInlineTextSource("read this later");

        var asset = capture.AddExternalReferenceSource("https://example.test/a?b=1");

        asset.StorageKind.Should().Be(SourceAssetStorageKind.ExternalReference);
        asset.ExternalReference.Should().Be("https://example.test/a?b=1");
        asset.MediaType.Should().Be(SourceAsset.UriListMediaType);
        asset.TextPayload.Should().BeNull();
        capture.SourceAssets.Should().HaveCount(2);
        capture.CurrentText.Should().Be("read this later", "the current text ignores non-text assets");
    }

    [Fact]
    public void FromQueueRequest_ShouldSeedSourcesAndAxesEvenWhenTheLegacyRowWasArchived()
    {
        // Archiving is a decision about the Inbox, not an erasure: a backfilled archived row must
        // still arrive with its material and its outcomes.
        var capture = Capture.FromQueueRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CaptureSource.Paste,
            contextBoardId: null,
            capturedAtClient: null,
            userTitle: "Archived one",
            legacyDisposition: CaptureDisposition.Archived,
            sourceText: "the archived material",
            externalReference: "https://example.test/source",
            processingSummary: CaptureProcessingSummary.Ready,
            actionState: CaptureActionState.Acted);

        capture.Disposition.Should().Be(CaptureUserDisposition.Archived);
        capture.ProcessingSummary.Should().Be(CaptureProcessingSummary.Ready);
        capture.ActionState.Should().Be(CaptureActionState.Acted);
        capture.Timeline.Should().Be(CaptureTimelineStep.Archived);
        capture.CurrentText.Should().Be("the archived material");
        capture.SourceAssets.Should().HaveCount(2);
        capture.SourceAssets[1].StorageKind.Should().Be(SourceAssetStorageKind.ExternalReference);
    }

    [Fact]
    public void FromQueueRequest_ShouldPreferAnExplicitDispositionAxisOverTheLegacyReceipt()
    {
        var capture = Capture.FromQueueRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CaptureSource.Typed,
            contextBoardId: null,
            capturedAtClient: null,
            userTitle: null,
            legacyDisposition: CaptureDisposition.Kept,
            userDisposition: CaptureUserDisposition.Archived);

        capture.Disposition.Should().Be(CaptureUserDisposition.Archived);
    }
}
