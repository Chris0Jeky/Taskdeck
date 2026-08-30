using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class CaptureTests
{
    private static Capture NewCapture(Guid? id = null, Guid? boardId = null, string? title = null) =>
        new(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            CaptureModality.Text,
            CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human,
            CaptureIntentMode.Organize,
            CaptureSource.Typed,
            boardId,
            userTitle: title);

    [Fact]
    public void Constructor_ShouldPreserveExplicitIdAndStartReceived()
    {
        var id = Guid.NewGuid();

        var capture = NewCapture(id);

        capture.Id.Should().Be(id);
        capture.Lifecycle.Should().Be(CaptureLifecycleState.Received);
        capture.CapturedAtServer.Should().Be(capture.CreatedAt);
        capture.LegacyRequestId.Should().BeNull();
        capture.ContextBoardId.Should().BeNull();
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
        capture.Producer.Should().Be(CaptureProducerKind.Human);
        capture.Intent.Should().Be(CaptureIntentMode.Organize);
        capture.LegacySource.Should().Be(CaptureSource.Voice);
        capture.ContextBoardId.Should().Be(boardId);
        capture.CapturedAtClient.Should().Be(clientAt);
        capture.UserTitle.Should().Be("Standup notes");
    }

    [Fact]
    public void FromQueueRequest_ShouldHonourProducerOverride()
    {
        var capture = Capture.FromQueueRequest(
            Guid.NewGuid(), Guid.NewGuid(), CaptureSource.Import, null, null, null,
            producerOverride: CaptureProducerKind.Agent);

        capture.Producer.Should().Be(CaptureProducerKind.Agent);
        capture.OriginAdapter.Should().Be(CaptureOriginAdapter.Import);
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyIdentifiers()
    {
        var emptyId = () => NewCapture(Guid.Empty);
        var emptyBoard = () => NewCapture(boardId: Guid.Empty);
        var emptyUser = () => new Capture(
            Guid.NewGuid(), Guid.Empty, CaptureModality.Text, CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human, CaptureIntentMode.Organize, CaptureSource.Typed);

        emptyId.Should().Throw<DomainException>().WithMessage("*Capture ID*");
        emptyBoard.Should().Throw<DomainException>().WithMessage("*Board ID*");
        emptyUser.Should().Throw<DomainException>().WithMessage("*User ID*");
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
        var control = () => NewCapture(title: "badtitle");

        tooLong.Should().Throw<DomainException>().WithMessage($"*{Capture.MaxUserTitleLength}*");
        control.Should().Throw<DomainException>().WithMessage("*control characters*");
    }

    [Fact]
    public void TransitionTo_ShouldFollowTheLifecyclePolicy()
    {
        var capture = NewCapture();

        capture.TransitionTo(CaptureLifecycleState.Preparing);
        capture.TransitionTo(CaptureLifecycleState.Understood);
        capture.TransitionTo(CaptureLifecycleState.Routed);
        capture.TransitionTo(CaptureLifecycleState.NeedsReview);
        capture.TransitionTo(CaptureLifecycleState.Acted);
        capture.TransitionTo(CaptureLifecycleState.Archived);

        capture.Lifecycle.Should().Be(CaptureLifecycleState.Archived);
        CaptureLifecyclePolicy.IsTerminal(CaptureLifecycleState.Archived).Should().BeTrue();
    }

    [Fact]
    public void TransitionTo_ShouldRejectSkippedOrTerminalMoves()
    {
        var fresh = NewCapture();
        var skip = () => fresh.TransitionTo(CaptureLifecycleState.Acted);
        skip.Should().Throw<DomainException>().WithMessage("*Received to Acted*");

        var archived = NewCapture();
        archived.TransitionTo(CaptureLifecycleState.Archived);
        var revive = () => archived.TransitionTo(CaptureLifecycleState.Preparing);
        revive.Should().Throw<DomainException>().WithMessage("*Archived to Preparing*");
    }

    [Fact]
    public void TransitionTo_SameState_ShouldBeANoOp()
    {
        var capture = NewCapture();
        var updatedAt = capture.UpdatedAt;

        capture.TransitionTo(CaptureLifecycleState.Received);

        capture.Lifecycle.Should().Be(CaptureLifecycleState.Received);
        capture.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void Failed_ShouldRemainRetryableAndKeepable()
    {
        var capture = NewCapture();
        capture.TransitionTo(CaptureLifecycleState.Preparing);
        capture.TransitionTo(CaptureLifecycleState.Failed);

        CaptureLifecyclePolicy.CanTransition(CaptureLifecycleState.Failed, CaptureLifecycleState.Preparing).Should().BeTrue();
        CaptureLifecyclePolicy.CanTransition(CaptureLifecycleState.Failed, CaptureLifecycleState.Kept).Should().BeTrue();
        CaptureLifecyclePolicy.CanTransition(CaptureLifecycleState.Failed, CaptureLifecycleState.Acted).Should().BeFalse();
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

    [Fact]
    public void SetIntent_ShouldRecordTheNewMode()
    {
        var capture = NewCapture();

        capture.SetIntent(CaptureIntentMode.Remember);

        capture.Intent.Should().Be(CaptureIntentMode.Remember);
        var act = () => capture.SetIntent((CaptureIntentMode)42);
        act.Should().Throw<DomainException>();
    }
}
