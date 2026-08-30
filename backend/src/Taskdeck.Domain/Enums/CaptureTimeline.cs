namespace Taskdeck.Domain.Enums;

/// <summary>
/// The one-line, user-legible step a capture is at — the timeline CF-20 renders
/// (<c>Received → Preparing → Understood → Needs input / Needs review → Acted</c>, with
/// <c>Kept</c>, <c>Failed</c> and <c>Archived</c> as the resting states). It is a
/// <b>projection</b> over the three orthogonal state axes, computed by <see cref="CaptureTimeline"/>,
/// never persisted as the only truth (ADR-0065 §Decision 1, amended 2026-08-30).
/// </summary>
public enum CaptureTimelineStep
{
    Received = 0,
    Preparing = 1,
    Understood = 2,
    NeedsInput = 3,
    NeedsReview = 4,
    Acted = 5,
    Kept = 6,
    Failed = 7,
    Archived = 8
}

/// <summary>
/// Pure projection from (<see cref="CaptureUserDisposition"/>, <see cref="CaptureProcessingSummary"/>,
/// <see cref="CaptureActionState"/>) to the timeline step. Precedence, highest first: the user's
/// disposition (archived / kept), then what was done (acted), then what the user must do (input /
/// review), then processing (failed / preparing), then whether anything is ready to understand.
/// <see cref="CaptureProcessingSummary.Partial"/> is deliberately shown as
/// <see cref="CaptureTimelineStep.Understood"/> rather than <see cref="CaptureTimelineStep.Failed"/>:
/// something usable exists, and the failed leg is a per-asset detail the UI lists beneath the step.
/// </summary>
public static class CaptureTimeline
{
    public static CaptureTimelineStep Project(
        CaptureUserDisposition disposition,
        CaptureProcessingSummary processing,
        CaptureActionState action)
    {
        if (disposition == CaptureUserDisposition.Archived)
        {
            return CaptureTimelineStep.Archived;
        }

        if (action == CaptureActionState.Acted)
        {
            return CaptureTimelineStep.Acted;
        }

        if (disposition == CaptureUserDisposition.Kept)
        {
            return CaptureTimelineStep.Kept;
        }

        switch (action)
        {
            case CaptureActionState.NeedsInput:
                return CaptureTimelineStep.NeedsInput;
            case CaptureActionState.NeedsReview:
                return CaptureTimelineStep.NeedsReview;
        }

        return processing switch
        {
            CaptureProcessingSummary.Failed => CaptureTimelineStep.Failed,
            CaptureProcessingSummary.Processing => CaptureTimelineStep.Preparing,
            CaptureProcessingSummary.Ready => CaptureTimelineStep.Understood,
            CaptureProcessingSummary.Partial => CaptureTimelineStep.Understood,
            _ => CaptureTimelineStep.Received
        };
    }
}
