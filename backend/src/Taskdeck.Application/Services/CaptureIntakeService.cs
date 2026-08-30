using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// The one canonical writer of the durable <see cref="Capture"/> aggregate and its
/// <see cref="SourceAsset"/>s (ADR-0065 §Decision 1; CF-01 <c>#2255</c>). Every path that admits a
/// capture goes through <see cref="IntakeAsync"/> — <see cref="CaptureService.CreateAsync"/>,
/// <see cref="LlmQueueService.AddToQueueAsync"/> and the ID-preserving backfill
/// (<see cref="CaptureBackfillService"/>, through <see cref="BuildCapture"/>) — and nothing else
/// constructs a <see cref="Capture"/>; <c>CaptureIntakeIsTheOnlyCaptureWriterTests</c> proves it
/// over the source tree.
/// <para>
/// <b>Native intake, not a queue mirror.</b> The order is the ADR's: the sources are stored first
/// and the capture is valid the moment they are (a capture never becomes unreadable because a job
/// failed), and the queue row is created beside it as the <i>job record</i> that CF-03 will replace.
/// The capture takes the queue row's own id (ID-preserving), so every existing
/// <c>CreatedFromCaptureId</c> / <c>CaptureItemId</c> reference keeps resolving.
/// </para>
/// <para>
/// Both are staged into the ambient unit of work, so they commit together or not at all, and intake
/// must never fail where the queue row succeeds. While
/// <see cref="ContextFabricSettings.DualWriteCaptures"/> is off this class does nothing and shipped
/// behaviour is byte-identical.
/// </para>
/// </summary>
public sealed class CaptureIntakeService
{
    private readonly ICaptureStore? _captureStore;
    private readonly ContextFabricSettings _settings;

    public CaptureIntakeService(ICaptureStore? captureStore, ContextFabricSettings? settings)
    {
        _captureStore = captureStore;
        _settings = settings ?? new ContextFabricSettings();
    }

    /// <summary>True when the durable aggregate is written; false leaves shipped behaviour byte-identical.</summary>
    public bool DualWriteEnabled => _settings.DualWriteCaptures && _captureStore is not null;

    /// <summary>
    /// Admits a capture: builds the aggregate under <paramref name="request"/>'s id with its
    /// immutable sources and stages it beside the queue row. Returns the staged capture, or null
    /// when dual-write is off. The producer dimension comes from <see cref="CaptureSourceMapping"/>
    /// unless the caller knows the authenticated principal kind (an MCP agent, an integration
    /// connector) and passes it.
    /// </summary>
    public async Task<Capture?> IntakeAsync(
        LlmRequest request,
        CapturePayloadV1 payload,
        Guid userId,
        Guid? boardId,
        CaptureProducerKind? producerOverride = null,
        Guid? producedByPrincipalId = null,
        CancellationToken cancellationToken = default)
    {
        if (!DualWriteEnabled)
        {
            return null;
        }

        var capture = BuildCapture(request, payload, userId, boardId, producerOverride, producedByPrincipalId);
        await _captureStore!.AddAsync(capture, cancellationToken);
        return capture;
    }

    /// <summary>
    /// Builds the aggregate for a capture-shaped queue row. Shared by live intake and the
    /// ID-preserving backfill so both produce the identical shape: the sources first (the typed or
    /// pasted text as an immutable inline asset, and the user's locator as an
    /// <see cref="SourceAssetStorageKind.ExternalReference"/> asset when the payload carries one),
    /// then the state.
    /// <para>
    /// <paramref name="legacyState"/> is the three-axis state derived from what the queue row
    /// actually recorded; live intake leaves it null and takes the aggregate's own defaults
    /// (<c>Active</c> / <c>Idle</c> / <c>Unplanned</c>), while the backfill passes the state a
    /// pre-existing row earned. Nothing is defaulted to <c>Received</c> for a legacy row.
    /// </para>
    /// </summary>
    public static Capture BuildCapture(
        LlmRequest request,
        CapturePayloadV1 payload,
        Guid userId,
        Guid? boardId,
        CaptureProducerKind? producerOverride = null,
        Guid? producedByPrincipalId = null,
        CaptureLegacyState? legacyState = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(payload);

        return Capture.FromQueueRequest(
            request.Id,
            userId,
            payload.Source,
            boardId,
            payload.ClientCreatedAt,
            payload.TitleHint,
            requestedIntent: ResolveRequestedIntent(payload.Disposition),
            producerOverride: producerOverride,
            capturedAtServer: request.CreatedAt,
            legacyDisposition: payload.Disposition?.Kind,
            producedByPrincipalId: producedByPrincipalId,
            // The legacy contract rejects blank text before this point; the guard only keeps intake
            // from ever being the reason a capture fails.
            sourceText: string.IsNullOrWhiteSpace(payload.Text) ? null : payload.Text,
            externalReference: string.IsNullOrWhiteSpace(payload.ExternalRef) ? null : payload.ExternalRef,
            processingSummary: legacyState?.ProcessingSummary,
            actionState: legacyState?.ActionState,
            userDisposition: legacyState?.Disposition);
    }

    /// <summary>
    /// <c>ProposalRequested</c> is today's <see cref="CaptureIntentMode.Act"/> path and <c>Kept</c>
    /// is <see cref="CaptureIntentMode.Remember"/> (CF-02); a row without a recorded disposition is
    /// the default <see cref="CaptureIntentMode.Organize"/> — understood, not yet planned.
    /// </summary>
    public static CaptureIntentMode ResolveRequestedIntent(CaptureDispositionV1? disposition) =>
        disposition?.Kind switch
        {
            CaptureDisposition.ProposalRequested => CaptureIntentMode.Act,
            CaptureDisposition.Kept => CaptureIntentMode.Remember,
            _ => CaptureIntentMode.Organize
        };
}
