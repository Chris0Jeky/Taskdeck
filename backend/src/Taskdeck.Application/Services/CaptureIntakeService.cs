using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// The one canonical writer of the durable <see cref="Capture"/> aggregate and its
/// <see cref="SourceAsset"/>s (ADR-0065 §Decision 1; CF-01 <c>#2255</c>). Every path that admits a
/// capture goes through <see cref="IntakeAsync"/> or <see cref="StageMemorySourcesAsync"/> — <see cref="CaptureService.CreateAsync"/>,
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
/// <see cref="ContextFabricSettings.DualWriteCaptures"/> is off legacy intake does nothing. Native
/// private memory sources use their own admission path, independent of the legacy mirror switch.
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

    /// <summary>Retains an explicit private audio original and its question; does not schedule transcription.</summary>
    public async Task<(Capture Capture, SourceAsset Audio)> StageAudioAnswerAsync(Guid ownerId, Guid boardId,
        string title, string evidence, BlobReference blob, string mediaType, string fileName, CancellationToken ct)
    {
        var store = _captureStore ?? throw new InvalidOperationException("Audio originals require a capture store.");
        if (blob.OwnerUserId != ownerId || blob.AssetModality != CaptureModality.Audio)
            throw new DomainException(ErrorCodes.ValidationError, "The recording belongs to a different source.");
        var capture = new Capture(Guid.NewGuid(), ownerId, CaptureModality.Audio, CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human, CaptureIntentMode.Remember, CaptureSource.Voice,
            contextBoardId: boardId, userTitle: title, userNote: "Private audio original. No transcription has been requested.");
        var audio = SourceAsset.FromBlobReference(capture.Id, 0, CaptureModality.Audio, mediaType, blob.ContentHash,
            blob.ByteSize, blob.ReferenceId, fileName);
        capture.AddSourceAsset(audio);
        capture.AddInlineTextSource(evidence, originalName: "original-question-evidence.txt");
        capture.Keep();
        await store.AddAsync(capture, ct);
        return (capture, audio);
    }

    /// <summary>
    /// Stages native private memory sources in the memory's unit of work. This is not a legacy
    /// queue mirror and never schedules processing. Historical rows are admitted on their next
    /// explicit write; their saved text remains the authority, not a reconstructed question.
    /// Existing memories must advance their concurrency revision before staging an admission.
    /// </summary>
    public async Task StageMemorySourcesAsync(WorkspaceMemory memory, CancellationToken ct = default)
    {
        var store = _captureStore ?? throw new InvalidOperationException("Private memory sources require a capture store.");
        Capture capture;
        if (memory.SourceCaptureId is { } captureId)
        {
            capture = await store.GetByIdForUpdateAsync(captureId, memory.UserId, ct)
                ?? throw new DomainException(ErrorCodes.Conflict, "The private source is unavailable. Keep your draft and reload.");
            if (capture.LegacyRequestId.HasValue || capture.ContextBoardId != memory.BoardId)
                throw new DomainException(ErrorCodes.Conflict, "The private source no longer matches this memory.");
            if (capture.CurrentText != memory.Text)
            {
                var answer = capture.SupersedeInlineTextSource(memory.Text, originalName: $"answer-revision-{memory.Revision}.txt");
                memory.RecordSources(capture.Id, answer.Id, memory.EvidenceSourceAssetId);
            }
            await store.UpdateAsync(capture, ct);
            return;
        }

        capture = new Capture(Guid.NewGuid(), memory.UserId, CaptureModality.Text,
            CaptureOriginAdapter.WebComposer, CaptureProducerKind.Human, CaptureIntentMode.Remember,
            CaptureSource.Typed, contextBoardId: memory.BoardId, userTitle: memory.Title,
            userNote: $"Private memory {memory.Id}. Originals retained until account deletion.");
        var evidence = string.IsNullOrWhiteSpace(memory.OriginalEvidence) ? null
            : capture.AddInlineTextSource(memory.OriginalEvidence, originalName: "original-question-evidence.txt");
        SourceAsset? previous = null;
        foreach (var revision in memory.History.OrderBy(x => x.Revision))
        {
            if (previous?.TextPayload?.Text != revision.Text)
                previous = previous is null
                    ? capture.AddInlineTextSource(revision.Text, originalName: $"answer-revision-{revision.Revision}.txt")
                    : capture.SupersedeInlineTextSource(revision.Text, originalName: $"answer-revision-{revision.Revision}.txt");
            revision.RecordAnswerSource(previous!.Id);
        }
        if (previous?.TextPayload?.Text != memory.Text)
            previous = previous is null
                ? capture.AddInlineTextSource(memory.Text, originalName: $"answer-revision-{memory.Revision}.txt")
                : capture.SupersedeInlineTextSource(memory.Text, originalName: $"answer-revision-{memory.Revision}.txt");
        memory.RecordSources(capture.Id, previous!.Id, evidence?.Id);
        capture.Keep();
        await store.AddAsync(capture, ct);
    }

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
            producedByPrincipalId: producedByPrincipalId,
            // The legacy contract rejects blank text before this point; the guard only keeps intake
            // from ever being the reason a capture fails.
            sourceText: string.IsNullOrWhiteSpace(payload.Text) ? null : payload.Text,
            externalReference: string.IsNullOrWhiteSpace(payload.ExternalRef) ? null : payload.ExternalRef,
            processingSummary: legacyState?.ProcessingSummary,
            actionState: legacyState?.ActionState,
            userDisposition: legacyState?.Disposition);
        capture.RecordLegacyReconciliation(request.UpdatedAt);
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
