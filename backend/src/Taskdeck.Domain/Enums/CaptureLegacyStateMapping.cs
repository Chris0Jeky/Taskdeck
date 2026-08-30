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
/// picking a single winner. The mapping below is the same one the
/// <c>ReconcileContextFabricScaffold</c> migration documents, composed with the shipped
/// <see cref="CaptureStatusPolicy.MapFromQueueStatus"/>: queue status maps to the retired lifecycle
/// value (<c>Pending→Received</c>, <c>Processing→Preparing</c>, <c>Completed→Understood</c> or
/// <c>NeedsReview</c> with a proposal, converted<c>→Acted</c>, <c>Cancelled→Archived</c>,
/// <c>Failed→Failed</c>) and that value maps to the axes exactly as the migration's SQL does
/// (<c>Kept→Kept</c>, <c>Archived→Archived</c>; <c>Preparing→Processing</c>,
/// <c>Understood/Routed/NeedsReview/Acted→Ready</c>, <c>Failed→Failed</c>;
/// <c>NeedsReview→NeedsReview</c>, <c>Acted→Acted</c>).
/// </para>
/// <para>
/// It differs from that composition in exactly one respect, deliberately: a <b>cancelled</b> row
/// that had already produced a proposal or an applied change keeps its processing and action
/// outcomes instead of collapsing to <c>Idle</c>/<c>Unplanned</c>. Archiving is a decision about the
/// Inbox; it does not make it untrue that the capture was understood or acted on (ADR-0065
/// §Decision 1). The one-line <see cref="CaptureTimeline"/> still shows <c>Archived</c>, so nothing
/// a user sees changes.
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
