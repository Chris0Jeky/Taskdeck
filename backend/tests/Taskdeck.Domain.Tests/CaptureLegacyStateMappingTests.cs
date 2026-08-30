using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Domain.Tests;

/// <summary>
/// CF-01 (#2255): the ID-preserving backfill derives the three durable state axes from what a legacy
/// queue row actually recorded - never a single lifecycle value, and never a default <c>Received</c>
/// for a row that had already been triaged, proposed, applied, kept or put away.
/// </summary>
public sealed class CaptureLegacyStateMappingTests
{
    [Theory]
    // A fresh Inbox row.
    [InlineData(RequestStatus.Pending, false, false, null,
        CaptureUserDisposition.Active, CaptureProcessingSummary.Idle, CaptureActionState.Unplanned,
        CaptureTimelineStep.Received)]
    // Triage is running.
    [InlineData(RequestStatus.Processing, false, false, null,
        CaptureUserDisposition.Active, CaptureProcessingSummary.Processing, CaptureActionState.Unplanned,
        CaptureTimelineStep.Preparing)]
    // Triaged with nothing proposed.
    [InlineData(RequestStatus.Completed, false, false, null,
        CaptureUserDisposition.Active, CaptureProcessingSummary.Ready, CaptureActionState.Unplanned,
        CaptureTimelineStep.Understood)]
    // A proposal is waiting for the human decision (review-first).
    [InlineData(RequestStatus.Completed, true, false, null,
        CaptureUserDisposition.Active, CaptureProcessingSummary.Ready, CaptureActionState.NeedsReview,
        CaptureTimelineStep.NeedsReview)]
    // The proposal was applied.
    [InlineData(RequestStatus.Completed, true, true, null,
        CaptureUserDisposition.Active, CaptureProcessingSummary.Ready, CaptureActionState.Acted,
        CaptureTimelineStep.Acted)]
    // Triage failed: the sources stay readable and retryable, and nothing is planned.
    [InlineData(RequestStatus.Failed, false, false, null,
        CaptureUserDisposition.Active, CaptureProcessingSummary.Failed, CaptureActionState.Unplanned,
        CaptureTimelineStep.Failed)]
    // Ignored/cancelled with nothing produced.
    [InlineData(RequestStatus.Cancelled, false, false, null,
        CaptureUserDisposition.Archived, CaptureProcessingSummary.Idle, CaptureActionState.Unplanned,
        CaptureTimelineStep.Archived)]
    // Archived AFTER it was applied: the outcome is not erased by the decision to put it away.
    [InlineData(RequestStatus.Cancelled, true, true, CaptureDisposition.Archived,
        CaptureUserDisposition.Archived, CaptureProcessingSummary.Ready, CaptureActionState.Acted,
        CaptureTimelineStep.Archived)]
    // Kept for later while still pending.
    [InlineData(RequestStatus.Pending, false, false, CaptureDisposition.Kept,
        CaptureUserDisposition.Kept, CaptureProcessingSummary.Idle, CaptureActionState.Unplanned,
        CaptureTimelineStep.Kept)]
    // A proposal was requested: the user is active, not kept or archived.
    [InlineData(RequestStatus.Processing, false, false, CaptureDisposition.ProposalRequested,
        CaptureUserDisposition.Active, CaptureProcessingSummary.Processing, CaptureActionState.Unplanned,
        CaptureTimelineStep.Preparing)]
    public void Resolve_ShouldDeriveEachAxisFromWhatTheQueueRowRecorded(
        RequestStatus queueStatus,
        bool hasLinkedProposal,
        bool isConverted,
        CaptureDisposition? legacyDisposition,
        CaptureUserDisposition expectedDisposition,
        CaptureProcessingSummary expectedProcessing,
        CaptureActionState expectedAction,
        CaptureTimelineStep expectedTimeline)
    {
        var state = CaptureLegacyStateMapping.Resolve(
            queueStatus,
            hasLinkedProposal,
            isConverted,
            legacyDisposition);

        state.Disposition.Should().Be(expectedDisposition);
        state.ProcessingSummary.Should().Be(expectedProcessing);
        state.ActionState.Should().Be(expectedAction);
        CaptureTimeline.Project(state.Disposition, state.ProcessingSummary, state.ActionState)
            .Should().Be(expectedTimeline);
    }

    [Fact]
    public void Resolve_ShouldNotDefaultAnAlreadyProgressedRowToReceived()
    {
        // The whole point of the mapping: a backfilled row must never look brand new.
        var progressed = new[]
        {
            CaptureLegacyStateMapping.Resolve(RequestStatus.Processing, false, false, null),
            CaptureLegacyStateMapping.Resolve(RequestStatus.Completed, false, false, null),
            CaptureLegacyStateMapping.Resolve(RequestStatus.Completed, true, false, null),
            CaptureLegacyStateMapping.Resolve(RequestStatus.Completed, true, true, null),
            CaptureLegacyStateMapping.Resolve(RequestStatus.Failed, false, false, null),
            CaptureLegacyStateMapping.Resolve(RequestStatus.Cancelled, false, false, null)
        };

        progressed.Should().OnlyContain(state =>
            CaptureTimeline.Project(state.Disposition, state.ProcessingSummary, state.ActionState)
                != CaptureTimelineStep.Received);
    }

    [Fact]
    public void Resolve_ShouldBeTotalOverEveryQueueStatusAndDisposition()
    {
        foreach (var status in Enum.GetValues<RequestStatus>())
        {
            foreach (var disposition in Enum.GetValues<CaptureDisposition>().Cast<CaptureDisposition?>().Append(null))
            {
                var act = () => CaptureLegacyStateMapping.Resolve(status, false, false, disposition);
                act.Should().NotThrow($"queue status {status} with disposition {disposition} must map");
            }
        }
    }
}

/// <summary>The persisted marker that arms the CF-01 read switch.</summary>
public sealed class CaptureBackfillStateTests
{
    [Fact]
    public void ForLegacyQueue_ShouldStartIncompleteUnderTheFixedSingletonId()
    {
        var started = DateTimeOffset.UtcNow;

        var state = CaptureBackfillState.ForLegacyQueue(started);

        state.Id.Should().Be(CaptureBackfillState.LegacyQueueBackfillId);
        state.Key.Should().Be(CaptureBackfillState.LegacyQueueBackfillKey);
        state.StartedAt.Should().Be(started);
        state.IsComplete.Should().BeFalse("the read switch is disarmed until the backfill finishes");
    }

    [Fact]
    public void RecordBatch_ShouldAccumulateAcrossRunsAndKeepTheLastReason()
    {
        var state = CaptureBackfillState.ForLegacyQueue(DateTimeOffset.UtcNow);

        state.RecordBatch(10, 0);
        state.RecordBatch(5, 2, "DomainException: title too long");

        state.MigratedCount.Should().Be(15);
        state.SkippedCount.Should().Be(2);
        state.LastSkipReason.Should().Be("DomainException: title too long");
    }

    [Fact]
    public void MarkComplete_ShouldKeepTheFirstCompletionTime()
    {
        var state = CaptureBackfillState.ForLegacyQueue(DateTimeOffset.UtcNow);
        var first = DateTimeOffset.UtcNow.AddMinutes(-5);

        state.MarkComplete(first);
        state.MarkComplete(DateTimeOffset.UtcNow);

        state.CompletedAt.Should().Be(first, "that is when the read switch became safe");
        state.IsComplete.Should().BeTrue();
    }
}
