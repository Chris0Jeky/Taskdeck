using FluentAssertions;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class CaptureTimelineTests
{
    [Theory]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Idle, CaptureActionState.Unplanned, CaptureTimelineStep.Received)]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Processing, CaptureActionState.Unplanned, CaptureTimelineStep.Preparing)]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Ready, CaptureActionState.Unplanned, CaptureTimelineStep.Understood)]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Partial, CaptureActionState.Unplanned, CaptureTimelineStep.Understood)]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Failed, CaptureActionState.Unplanned, CaptureTimelineStep.Failed)]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Ready, CaptureActionState.NeedsInput, CaptureTimelineStep.NeedsInput)]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Partial, CaptureActionState.NeedsReview, CaptureTimelineStep.NeedsReview)]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Failed, CaptureActionState.NeedsReview, CaptureTimelineStep.NeedsReview)]
    [InlineData(CaptureUserDisposition.Active, CaptureProcessingSummary.Ready, CaptureActionState.Acted, CaptureTimelineStep.Acted)]
    [InlineData(CaptureUserDisposition.Kept, CaptureProcessingSummary.Idle, CaptureActionState.Unplanned, CaptureTimelineStep.Kept)]
    [InlineData(CaptureUserDisposition.Kept, CaptureProcessingSummary.Processing, CaptureActionState.NeedsReview, CaptureTimelineStep.Kept)]
    [InlineData(CaptureUserDisposition.Kept, CaptureProcessingSummary.Ready, CaptureActionState.Acted, CaptureTimelineStep.Kept)]
    [InlineData(CaptureUserDisposition.Archived, CaptureProcessingSummary.Ready, CaptureActionState.Acted, CaptureTimelineStep.Archived)]
    [InlineData(CaptureUserDisposition.Archived, CaptureProcessingSummary.Failed, CaptureActionState.Unplanned, CaptureTimelineStep.Archived)]
    public void Project_ShouldApplyThePrecedenceArchivedThenKeptThenActionThenProcessing(
        CaptureUserDisposition disposition,
        CaptureProcessingSummary processing,
        CaptureActionState action,
        CaptureTimelineStep expected)
    {
        CaptureTimeline.Project(disposition, processing, action).Should().Be(expected);
    }

    [Fact]
    public void Project_ShouldBeTotalOverEveryCombination()
    {
        foreach (var disposition in Enum.GetValues<CaptureUserDisposition>())
        foreach (var processing in Enum.GetValues<CaptureProcessingSummary>())
        foreach (var action in Enum.GetValues<CaptureActionState>())
        {
            var step = CaptureTimeline.Project(disposition, processing, action);

            Enum.IsDefined(step).Should().BeTrue($"({disposition}, {processing}, {action}) must project to a defined step");
        }
    }

    [Fact]
    public void Project_ShouldNeverShowFailedWhenSomethingUsableExists()
    {
        // A text-plus-screenshot capture whose image leg failed keeps its understood text.
        CaptureTimeline.Project(CaptureUserDisposition.Active, CaptureProcessingSummary.Partial, CaptureActionState.Unplanned)
            .Should().NotBe(CaptureTimelineStep.Failed);
    }
}
