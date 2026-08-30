using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// The one canonical writer of the durable <see cref="Capture"/> aggregate and its
/// <see cref="SourceAsset"/>s (ADR-0065 §Decision 1; CF-01 <c>#2255</c>; amended 2026-08-30 after
/// the external audit found a second creation path — <c>POST /api/llm-queue</c> — that bypassed the
/// dual-write seam). Every path that creates a capture-shaped queue row goes through
/// <see cref="MirrorLegacyCaptureAsync"/>: <see cref="CaptureService.CreateAsync"/> and
/// <see cref="LlmQueueService.AddToQueueAsync"/> today; CF-01 extends this class into the intake
/// that creates captures natively (assets first, then jobs) and retires the queue-row mirror.
/// <para>
/// While <see cref="ContextFabricSettings.DualWriteCaptures"/> is off (the default), this class
/// does nothing. While it is on, the mirror is staged into the ambient unit of work beside the queue
/// row so both commit together or not at all, and it must never fail where the queue row succeeds:
/// the typed or pasted text becomes an immutable inline <see cref="SourceAsset"/> (the raw material
/// no longer lives only on the processing job), the legacy disposition maps onto the user
/// disposition axis, and the requested intent is derived from what the legacy contract asked for.
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

    /// <summary>True when mirrors are written; false leaves shipped behaviour byte-identical.</summary>
    public bool DualWriteEnabled => _settings.DualWriteCaptures && _captureStore is not null;

    /// <summary>
    /// Stages the ID-preserving mirror of a legacy capture queue row (<paramref name="request"/>)
    /// and its inline text asset. Returns the staged capture, or null when dual-write is off.
    /// The producer dimension comes from <see cref="CaptureSourceMapping"/> unless the caller knows
    /// the authenticated principal kind (an MCP agent, an integration connector) and passes it.
    /// </summary>
    public async Task<Capture?> MirrorLegacyCaptureAsync(
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

        var capture = Capture.FromQueueRequest(
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
            producedByPrincipalId: producedByPrincipalId);

        // The legacy contract rejects blank text before this point; the guard only keeps the
        // mirror from ever being the reason a capture fails.
        if (!string.IsNullOrWhiteSpace(payload.Text))
        {
            capture.AddInlineTextSource(payload.Text);
        }

        await _captureStore!.AddAsync(capture, cancellationToken);
        return capture;
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
