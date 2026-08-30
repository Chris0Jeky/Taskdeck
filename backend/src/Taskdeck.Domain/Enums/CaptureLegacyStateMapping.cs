namespace Taskdeck.Domain.Enums;

/// <summary>
/// The three durable state axes a legacy capture queue row resolves to (CF-01 <c>#2255</c>).
/// </summary>
public readonly record struct CaptureLegacyState(
    CaptureUserDisposition Disposition,
    CaptureProcessingSummary ProcessingSummary,
    CaptureActionState ActionState);

/// <summary>
/// Derives the three orthogonal capture state axes from what a legacy queue row actually recorded:
/// its <see cref="RequestStatus"/>, whether a proposal was linked to it, whether that proposal was
/// applied (converted), and the explicit <see cref="CaptureDisposition"/> the user chose.
/// <para>
/// <b>Why not one value.</b> The retired <c>CaptureLifecycleState</c> fused these axes, and the
/// backfill must not resurrect that mistake by stamping every legacy row <c>Received</c> or by
/// picking a single winner.
/// </para>
/// <para>
/// <b>Its own table, not a composition.</b> It deliberately does <i>not</i> call
/// <see cref="CaptureStatusPolicy.MapFromQueueStatus"/>: that policy collapses a row into one
/// user-facing status, which is exactly the information loss the axes exist to undo - a converted
/// row reports only <c>Converted</c> and a cancelled row only <c>Ignored</c>, so the processing and
/// action facts underneath are gone. Each axis is read from the raw signals independently, and the
/// two answers therefore differ wherever the collapse would have lost something: a <c>Failed</c> row
/// that had already produced a proposal keeps <c>NeedsReview</c> on the action axis, and an applied
/// conversion sets <c>Acted</c> without hiding the queue status underneath it. The whole table:
/// </para>
/// <list type="table">
/// <listheader><term>Signal</term><description>Disposition, ProcessingSummary, ActionState</description></listheader>
/// <item><term>Pending</term><description>Active, Idle, Unplanned</description></item>
/// <item><term>Processing</term><description>Active, Processing, Unplanned</description></item>
/// <item><term>Completed</term><description>Active, Ready, Unplanned</description></item>
/// <item><term>Failed</term><description>Active, Failed, Unplanned</description></item>
/// <item><term>Cancelled</term><description>Archived, Idle, Unplanned</description></item>
/// <item><term>plus a linked proposal</term><description>action axis becomes NeedsReview; a cancelled row also moves its processing axis to Ready</description></item>
/// <item><term>plus an applied conversion</term><description>action axis becomes Acted; a cancelled row also moves its processing axis to Ready</description></item>
/// <item><term>plus a recorded disposition</term><description>the disposition axis takes CaptureUserDispositionMapping.FromLegacy, overriding the Archived default a cancelled row would otherwise get</description></item>
/// </list>
/// <para>
/// One departure from the axis mapping the <c>ReconcileContextFabricScaffold</c> migration documents
/// is deliberate: a <b>cancelled</b> row that had already produced a proposal or an applied change
/// keeps its processing and action outcomes instead of collapsing to <c>Idle</c> and
/// <c>Unplanned</c>. Archiving is a decision about the Inbox; it does not make it untrue that the
/// capture was understood or acted on (ADR-0065 Decision 1). The one-line
/// <see cref="CaptureTimeline"/> still shows <c>Archived</c>, so nothing a user sees changes.
/// </para>
/// </summary>
public static class CaptureLegacyStateMapping
{
    public static CaptureLegacyState Resolve(
        RequestStatus queueStatus,
        bool hasLinkedProposal,
        bool isConverted,
        CaptureDisposition? legacyDisposition)
    {
        var disposition = legacyDisposition.HasValue
            ? CaptureUserDispositionMapping.FromLegacy(legacyDisposition.Value)
            : queueStatus == RequestStatus.Cancelled
                ? CaptureUserDisposition.Archived
                : CaptureUserDisposition.Active;

        var action = isConverted
            ? CaptureActionState.Acted
            : hasLinkedProposal
                ? CaptureActionState.NeedsReview
                : CaptureActionState.Unplanned;

        var processing = queueStatus switch
        {
            RequestStatus.Failed => CaptureProcessingSummary.Failed,
            RequestStatus.Processing => CaptureProcessingSummary.Processing,
            RequestStatus.Completed => CaptureProcessingSummary.Ready,
            RequestStatus.Cancelled => hasLinkedProposal || isConverted
                ? CaptureProcessingSummary.Ready
                : CaptureProcessingSummary.Idle,
            _ => CaptureProcessingSummary.Idle
        };

        return new CaptureLegacyState(disposition, processing, action);
    }
}
