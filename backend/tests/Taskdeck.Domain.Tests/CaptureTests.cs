using System.Text;
using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class CaptureTests
{
    private static Capture NewCapture(
        Guid? id = null,
        Guid? boardId = null,
        string? title = null,
        CaptureIntentMode intent = CaptureIntentMode.Organize) =>
        new(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            CaptureModality.Text,
            CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human,
            intent,
            CaptureSource.Typed,
            boardId,
            userTitle: title);

    [Fact]
    public void Constructor_ShouldPreserveExplicitIdAndStartActiveIdleUnplanned()
    {
        var id = Guid.NewGuid();

        var capture = NewCapture(id);

        capture.Id.Should().Be(id);
        capture.Disposition.Should().Be(CaptureUserDisposition.Active);
        capture.ProcessingSummary.Should().Be(CaptureProcessingSummary.Idle);
        capture.ActionState.Should().Be(CaptureActionState.Unplanned);
        capture.Timeline.Should().Be(CaptureTimelineStep.Received);
        capture.RequestedIntent.Should().Be(CaptureIntentMode.Organize);
        capture.EffectiveIntent.Should().Be(CaptureIntentMode.Organize, "an explicit request is its own effective intent");
        capture.IntentResolvedByRunId.Should().BeNull();
        capture.ProducedByPrincipalId.Should().BeNull("the owner produced it");
        capture.CapturedAtServer.Should().Be(capture.CreatedAt);
        capture.LegacyRequestId.Should().BeNull();
        capture.ContextBoardId.Should().BeNull();
        capture.SourceAssets.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithAutoIntent_ShouldLeaveTheEffectiveIntentUnresolved()
    {
        var capture = NewCapture(intent: CaptureIntentMode.Auto);

        capture.RequestedIntent.Should().Be(CaptureIntentMode.Auto);
        capture.EffectiveIntent.Should().BeNull("Auto is an instruction to infer, not a result");
    }

    [Fact]
    public void FromQueueRequest_ShouldMirrorTheQueueRowIdAndMapDimensions()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var clientAt = DateTimeOffset.UtcNow.AddMinutes(-3);

        var capture = Capture.FromQueueRequest(requestId, userId, CaptureSource.Voice, boardId, clientAt, "  Standup notes ");

        capture.Id.Should().Be(requestId);
        capture.LegacyRequestId.Should().Be(requestId);
        capture.UserId.Should().Be(userId);
        capture.PrimaryModality.Should().Be(CaptureModality.Audio);
        capture.OriginAdapter.Should().Be(CaptureOriginAdapter.WebComposer);
        capture.ProducerKind.Should().Be(CaptureProducerKind.Human);
        capture.RequestedIntent.Should().Be(CaptureIntentMode.Organize);
        capture.LegacySourceSnapshot.Should().Be(CaptureSource.Voice);
        capture.ContextBoardId.Should().Be(boardId);
        capture.CapturedAtClient.Should().Be(clientAt);
        capture.UserTitle.Should().Be("Standup notes");
        capture.Disposition.Should().Be(CaptureUserDisposition.Active);

        capture.AddInlineTextSource("transcript pasted from the voice tool");
        capture.PrimaryModality.Should().Be(CaptureModality.Text, "the summary follows the first stored asset; the snapshot keeps the legacy Voice source");
        capture.LegacySourceSnapshot.Should().Be(CaptureSource.Voice);
    }

    [Fact]
    public void FromQueueRequest_ShouldTakeTheProducerFromTheMappingByDefault()
    {
        var imported = Capture.FromQueueRequest(Guid.NewGuid(), Guid.NewGuid(), CaptureSource.MarkdownImport, null, null, null);
        var meeting = Capture.FromQueueRequest(Guid.NewGuid(), Guid.NewGuid(), CaptureSource.MeetingIntegration, null, null, null);

        imported.ProducerKind.Should().Be(CaptureProducerKind.Human, "importing is a transport a person performs, not a principal kind");
        imported.OriginAdapter.Should().Be(CaptureOriginAdapter.Import);
        meeting.ProducerKind.Should().Be(CaptureProducerKind.Integration);
        meeting.OriginAdapter.Should().Be(CaptureOriginAdapter.Integration);
    }

    [Fact]
    public void FromQueueRequest_ShouldHonourAnExplicitProducerOverrideAndPrincipal()
    {
        var agentProfileId = Guid.NewGuid();

        var capture = Capture.FromQueueRequest(
            Guid.NewGuid(), Guid.NewGuid(), CaptureSource.Typed, null, null, null,
            producerOverride: CaptureProducerKind.Agent,
            producedByPrincipalId: agentProfileId);

        capture.ProducerKind.Should().Be(CaptureProducerKind.Agent);
        capture.ProducedByPrincipalId.Should().Be(agentProfileId);
        capture.OriginAdapter.Should().Be(CaptureOriginAdapter.WebComposer);
    }

    [Fact]
    public void FromQueueRequest_ShouldKeepTheQueueRowsIntakeTimeWhenSupplied()
    {
        var intake = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

        var capture = Capture.FromQueueRequest(
            Guid.NewGuid(), Guid.NewGuid(), CaptureSource.Typed, null, null, null, capturedAtServer: intake);

        capture.CapturedAtServer.Should().Be(intake);
        capture.CreatedAt.Should().Be(intake, "a backfilled capture keeps its chronological Inbox position");
        capture.UpdatedAt.Should().Be(intake);
    }

    [Theory]
    [InlineData(CaptureDisposition.Kept, CaptureUserDisposition.Kept)]
    [InlineData(CaptureDisposition.Archived, CaptureUserDisposition.Archived)]
    [InlineData(CaptureDisposition.ProposalRequested, CaptureUserDisposition.Active)]
    public void FromQueueRequest_ShouldMapTheLegacyDispositionOntoTheUserAxis(CaptureDisposition legacy, CaptureUserDisposition expected)
    {
        var capture = Capture.FromQueueRequest(
            Guid.NewGuid(), Guid.NewGuid(), CaptureSource.Typed, null, null, null, legacyDisposition: legacy);

        capture.Disposition.Should().Be(expected);
    }

    [Fact]
    public void LegacyDispositionMapping_ShouldCoverEveryLegacyValue()
    {
        foreach (var legacy in Enum.GetValues<CaptureDisposition>())
        {
            var act = () => CaptureUserDispositionMapping.FromLegacy(legacy);

            act.Should().NotThrow($"every legacy disposition needs a durable mapping ({legacy})");
        }
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyIdentifiers()
    {
        var emptyId = () => NewCapture(Guid.Empty);
        var emptyBoard = () => NewCapture(boardId: Guid.Empty);
        var emptyUser = () => new Capture(
            Guid.NewGuid(), Guid.Empty, CaptureModality.Text, CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human, CaptureIntentMode.Organize, CaptureSource.Typed);
        var emptyPrincipal = () => new Capture(
            Guid.NewGuid(), Guid.NewGuid(), CaptureModality.Text, CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human, CaptureIntentMode.Organize, CaptureSource.Typed,
            producedByPrincipalId: Guid.Empty);

        emptyId.Should().Throw<DomainException>().WithMessage("*Capture ID*");
        emptyBoard.Should().Throw<DomainException>().WithMessage("*Board ID*");
        emptyUser.Should().Throw<DomainException>().WithMessage("*User ID*");
        emptyPrincipal.Should().Throw<DomainException>().WithMessage("*Producer principal ID*");
    }

    [Fact]
    public void Constructor_ShouldRejectUndefinedEnumValues()
    {
        var act = () => new Capture(
            Guid.NewGuid(), Guid.NewGuid(), (CaptureModality)99, CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human, CaptureIntentMode.Organize, CaptureSource.Typed);

        act.Should().Throw<DomainException>().WithMessage("*modality*");
    }

    [Fact]
    public void Constructor_ShouldBoundAndTrimTitle()
    {
        NewCapture(title: "   ").UserTitle.Should().BeNull();
        NewCapture(title: " hello ").UserTitle.Should().Be("hello");

        var tooLong = () => NewCapture(title: new string('t', Capture.MaxUserTitleLength + 1));

        tooLong.Should().Throw<DomainException>().WithMessage($"*{Capture.MaxUserTitleLength}*");
    }

    [Fact]
    public void Constructor_ShouldSanitiseControlCharactersInsteadOfRejecting()
    {
        // The legacy capture contract accepts CR/CRLF/NUL/ESC in a title hint, so the mirror must too:
        // a dual-written row may never fail where the queue row succeeds. A title is single-line, so
        // LF and TAB flatten as well; a note keeps them.
        NewCapture(title: "bad\u0000title").UserTitle.Should().Be("bad title");
        NewCapture(title: "line one\r\nline two").UserTitle.Should().Be("line one  line two");
        NewCapture(title: "tab\tflattened").UserTitle.Should().Be("tab flattened");
        NewCapture(title: "\u001b\u0007").UserTitle.Should().BeNull("a title made only of control characters collapses to nothing");

        var note = new Capture(
            Guid.NewGuid(), Guid.NewGuid(), CaptureModality.Text, CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human, CaptureIntentMode.Organize, CaptureSource.Typed,
            userNote: "line one\r\nline two\tend");
        note.UserNote.Should().Be("line one \nline two\tend", "a note keeps LF and TAB and only strips the rest");
    }

    [Fact]
    public void Retitle_ShouldBeANoOpForTheSameTitle()
    {
        var capture = NewCapture(title: "same");
        var updatedAt = capture.UpdatedAt;

        capture.Retitle(" same ");

        capture.UserTitle.Should().Be("same");
        capture.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void AddInlineTextSource_ShouldAppendImmutableAssetsInOrder()
    {
        var capture = NewCapture();

        var first = capture.AddInlineTextSource("remember to book the venue");
        var second = capture.AddInlineTextSource("https://example.org/venue", SourceAsset.UriListMediaType, "Venue page");

        capture.SourceAssets.Should().Equal(first, second);
        capture.PrimaryModality.Should().Be(CaptureModality.Text, "the first asset decides the summary");
        first.Ordinal.Should().Be(0);
        second.Ordinal.Should().Be(1);
        first.CaptureId.Should().Be(capture.Id);
        first.Modality.Should().Be(CaptureModality.Text);
        first.StorageKind.Should().Be(SourceAssetStorageKind.InlineText);
        first.MediaType.Should().Be(SourceAsset.PlainTextMediaType);
        first.TextPayload!.Text.Should().Be("remember to book the venue");
        first.TextPayload.SourceAssetId.Should().Be(first.Id);
        first.ByteSize.Should().Be(Encoding.UTF8.GetByteCount("remember to book the venue"));
        first.ContentHash.Should().Be(SourceAsset.HashOf(Encoding.UTF8.GetBytes("remember to book the venue")));
        second.OriginalName.Should().Be("Venue page");
    }

    [Fact]
    public void AddSourceAsset_ShouldRejectForeignOrOutOfOrderAssets()
    {
        var capture = NewCapture();
        var other = NewCapture();

        var foreign = () => capture.AddSourceAsset(SourceAsset.FromInlineText(other.Id, 0, "text"));
        var outOfOrder = () => capture.AddSourceAsset(SourceAsset.FromInlineText(capture.Id, 1, "text"));

        foreign.Should().Throw<DomainException>().WithMessage("*different capture*");
        outOfOrder.Should().Throw<DomainException>().WithMessage("*ordinal must be 0*");
        capture.SourceAssets.Should().BeEmpty();
    }

    [Fact]
    public void AddSourceAsset_ShouldEnforceTheCap()
    {
        var capture = NewCapture();
        for (var index = 0; index < Capture.MaxSourceAssets; index++)
        {
            capture.AddInlineTextSource($"asset {index}");
        }

        var overflow = () => capture.AddInlineTextSource("one too many");

        overflow.Should().Throw<DomainException>().WithMessage($"*{Capture.MaxSourceAssets}*");
    }

    [Fact]
    public void Keep_Reactivate_Archive_ShouldMoveOnlyTheUserDispositionAxis()
    {
        var capture = NewCapture();
        capture.RecordProcessingSummary(CaptureProcessingSummary.Ready);

        capture.Keep();
        capture.Disposition.Should().Be(CaptureUserDisposition.Kept);
        capture.ProcessingSummary.Should().Be(CaptureProcessingSummary.Ready, "keeping does not erase what was derived");
        capture.Timeline.Should().Be(CaptureTimelineStep.Kept);
        capture.RecordActionState(CaptureActionState.Acted);
        capture.Timeline.Should().Be(CaptureTimelineStep.Kept, "the user's own disposition outranks the outcome on the one-line timeline");
        capture.RecordActionState(CaptureActionState.Unplanned);

        capture.Reactivate();
        capture.Disposition.Should().Be(CaptureUserDisposition.Active);
        capture.Timeline.Should().Be(CaptureTimelineStep.Understood);

        capture.Archive();
        capture.Disposition.Should().Be(CaptureUserDisposition.Archived);
        capture.Timeline.Should().Be(CaptureTimelineStep.Archived);
    }

    [Fact]
    public void Archived_ShouldBeTerminalForEveryMutationButIdempotentItself()
    {
        var capture = NewCapture();
        capture.Archive();
        var updatedAt = capture.UpdatedAt;

        capture.Archive();
        capture.UpdatedAt.Should().Be(updatedAt);

        var keep = () => capture.Keep();
        var reactivate = () => capture.Reactivate();
        var process = () => capture.RecordProcessingSummary(CaptureProcessingSummary.Processing);
        var plan = () => capture.RecordActionState(CaptureActionState.NeedsReview);
        var addSource = () => capture.AddInlineTextSource("late text");
        var reintend = () => capture.SetRequestedIntent(CaptureIntentMode.Act);
        var retitle = () => capture.Retitle("new title");
        var recontext = () => capture.SetContextBoard(Guid.NewGuid());

        keep.Should().Throw<DomainException>().WithMessage("*archived*");
        retitle.Should().Throw<DomainException>().WithMessage("*archived*");
        recontext.Should().Throw<DomainException>().WithMessage("*archived*");
        reactivate.Should().Throw<DomainException>().WithMessage("*archived*");
        process.Should().Throw<DomainException>().WithMessage("*archived*");
        plan.Should().Throw<DomainException>().WithMessage("*archived*");
        addSource.Should().Throw<DomainException>().WithMessage("*archived*");
        reintend.Should().Throw<DomainException>().WithMessage("*archived*");
    }

    [Fact]
    public void Archive_ShouldPreserveTheActionOutcome()
    {
        var capture = NewCapture();
        capture.RecordProcessingSummary(CaptureProcessingSummary.Ready);
        capture.RecordActionState(CaptureActionState.Acted);

        capture.Archive();

        capture.ActionState.Should().Be(CaptureActionState.Acted, "archiving a capture does not make the applied change untrue");
        capture.Timeline.Should().Be(CaptureTimelineStep.Archived);
    }

    [Fact]
    public void ProcessingAndActionProjections_ShouldDriveTheTimeline()
    {
        var capture = NewCapture();

        capture.RecordProcessingSummary(CaptureProcessingSummary.Processing);
        capture.Timeline.Should().Be(CaptureTimelineStep.Preparing);

        capture.RecordProcessingSummary(CaptureProcessingSummary.Partial);
        capture.Timeline.Should().Be(CaptureTimelineStep.Understood, "a partially processed capture has something usable");

        capture.RecordProcessingSummary(CaptureProcessingSummary.Failed);
        capture.Timeline.Should().Be(CaptureTimelineStep.Failed);

        capture.RecordProcessingSummary(CaptureProcessingSummary.Ready);
        capture.RecordActionState(CaptureActionState.NeedsInput);
        capture.Timeline.Should().Be(CaptureTimelineStep.NeedsInput);

        capture.RecordActionState(CaptureActionState.NeedsReview);
        capture.Timeline.Should().Be(CaptureTimelineStep.NeedsReview);

        capture.RecordActionState(CaptureActionState.Acted);
        capture.Timeline.Should().Be(CaptureTimelineStep.Acted);
    }

    [Fact]
    public void RecordProcessingSummary_SameValue_ShouldNotTouch()
    {
        var capture = NewCapture();
        var updatedAt = capture.UpdatedAt;

        capture.RecordProcessingSummary(CaptureProcessingSummary.Idle);
        capture.RecordActionState(CaptureActionState.Unplanned);

        capture.UpdatedAt.Should().Be(updatedAt);

        var undefined = () => capture.RecordProcessingSummary((CaptureProcessingSummary)77);
        undefined.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetRequestedIntent_ShouldResetTheEffectiveIntentAndResolutionRun()
    {
        var capture = NewCapture(intent: CaptureIntentMode.Auto);
        capture.ResolveIntent(CaptureIntentMode.Organize, Guid.NewGuid());

        capture.SetRequestedIntent(CaptureIntentMode.Act);
        capture.RequestedIntent.Should().Be(CaptureIntentMode.Act);
        capture.EffectiveIntent.Should().Be(CaptureIntentMode.Act);
        capture.IntentResolvedByRunId.Should().BeNull();

        capture.SetRequestedIntent(CaptureIntentMode.Auto);
        capture.EffectiveIntent.Should().BeNull();

        var undefined = () => capture.SetRequestedIntent((CaptureIntentMode)42);
        undefined.Should().Throw<DomainException>();
    }

    [Fact]
    public void ResolveIntent_ShouldRecordTheRunForAnAutoRequestOnly()
    {
        var auto = NewCapture(intent: CaptureIntentMode.Auto);
        var runId = Guid.NewGuid();

        auto.ResolveIntent(CaptureIntentMode.Act, runId);

        auto.RequestedIntent.Should().Be(CaptureIntentMode.Auto);
        auto.EffectiveIntent.Should().Be(CaptureIntentMode.Act);
        auto.IntentResolvedByRunId.Should().Be(runId);

        var resolveToAuto = () => auto.ResolveIntent(CaptureIntentMode.Auto, Guid.NewGuid());
        var emptyRun = () => auto.ResolveIntent(CaptureIntentMode.Act, Guid.Empty);
        var explicitRequest = () => NewCapture().ResolveIntent(CaptureIntentMode.Act, Guid.NewGuid());

        resolveToAuto.Should().Throw<DomainException>().WithMessage("*Remember, Organize or Act*");
        emptyRun.Should().Throw<DomainException>().WithMessage("*Run ID*");
        explicitRequest.Should().Throw<DomainException>().WithMessage("*Only an Auto request*");
    }

    [Fact]
    public void SetContextBoard_ShouldAllowClearingButNotEmptyGuid()
    {
        var capture = NewCapture(boardId: Guid.NewGuid());

        capture.SetContextBoard(null);
        capture.ContextBoardId.Should().BeNull();

        var act = () => capture.SetContextBoard(Guid.Empty);
        act.Should().Throw<DomainException>().WithMessage("*Board ID*");
    }
}
