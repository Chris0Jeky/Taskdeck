using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>The outcome of one <see cref="CaptureBackfillService.RunAsync"/> call.</summary>
/// <param name="Ran">False when the pass is disabled by configuration.</param>
/// <param name="Migrated">Legacy rows brought into the aggregate for the first time.</param>
/// <param name="Reconciled">Existing captures whose queue row had moved past them and were brought back into agreement.</param>
/// <param name="Skipped">Distinct rows this run could not map; each stays readable through its queue row.</param>
/// <param name="Remaining">Rows still outstanding after the run; the marker completes only at zero.</param>
/// <param name="Complete">
/// Whether THIS run left the backlog empty. Deliberately not the marker: completion is sticky once
/// earned, because disarming the whole Inbox read switch over one row that arrived unmappable later
/// would be worse than the per-item queue-row fallback that already covers it. This flag is the
/// honest answer to "did the pass finish", which is what a caller and a test want to know.
/// </param>
public readonly record struct CaptureBackfillResult(
    bool Ran,
    int Migrated,
    int Reconciled,
    int Skipped,
    int Remaining,
    bool Complete);

/// <summary>
/// The ID-preserving backfill and ongoing reconcile pass between the legacy capture queue rows and
/// the durable <see cref="Capture"/> aggregate (ADR-0065 Decision 1, ruling 7; CF-01 <c>#2255</c>).
/// <para>
/// <b>Why C# and not SQL in a migration.</b> The state a legacy row carries lives in a serialised
/// <see cref="CapturePayloadV1"/> - source, text, title hint, external reference, provenance and
/// disposition are JSON, not columns - and the mapping onto the aggregate is domain logic
/// (<see cref="CaptureSourceMapping"/>, <see cref="CaptureLegacyStateMapping"/>,
/// <see cref="CaptureIntakeService.BuildCapture"/>). Re-implementing JSON parsing and those mappings
/// in raw SQL inside a migration would fork the rules and silently drift from them; the migration
/// owns the <i>schema</i> and this step owns the <i>data</i>. It runs after migrations at startup,
/// on every host that applies them.
/// </para>
/// <para>
/// <b>Backfill and reconcile are the same pass.</b> The backlog is a divergence join, so a row is
/// picked up when it has no capture <i>or</i> when the queue row has been written since its capture
/// last was. That second case is what protects the read switch: an operator who turns dual-write
/// off, lets a user edit a capture and turns it back on would otherwise leave the aggregate holding
/// pre-edit text, and the Inbox would serve it. A changed source is reconciled by appending a
/// superseding asset, never by rewriting one.
/// </para>
/// <para>
/// <b>Idempotent and resumable</b> by construction: a row leaves the backlog the moment its capture
/// agrees with it, so re-running creates nothing twice and a crash mid-way resumes at the next
/// outstanding row. Each batch commits in its own unit of work together with the progress marker,
/// and the change tracker is released afterwards so a long run does not accumulate every text
/// payload it has written.
/// </para>
/// <para>
/// <b>A row that cannot be mapped is never fatal and never blocking.</b> It is counted, logged by id
/// (never by content) and excluded from the rest of the run, so the healthy rows behind it are still
/// reached. It does keep the marker incomplete: while any row is outstanding the Inbox keeps reading
/// queue rows, which is the shipped behaviour and strictly safer than arming a read switch over a
/// database this pass could not fully account for.
/// </para>
/// </summary>
public sealed class CaptureBackfillService
{
    public const int DefaultBatchSize = 200;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICaptureStore _captureStore;
    private readonly ICaptureBackfillStore _backfillStore;
    private readonly ContextFabricSettings _settings;
    private readonly ILogger<CaptureBackfillService>? _logger;

    public CaptureBackfillService(
        IUnitOfWork unitOfWork,
        ICaptureStore captureStore,
        ICaptureBackfillStore backfillStore,
        ContextFabricSettings? settings = null,
        ILogger<CaptureBackfillService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _captureStore = captureStore;
        _backfillStore = backfillStore;
        _settings = settings ?? new ContextFabricSettings();
        _logger = logger;
    }

    /// <summary>
    /// Drains the backlog and records completion. Safe to call on every startup: a database whose
    /// captures all agree with their queue rows costs one marker read and one indexed count.
    /// </summary>
    public async Task<CaptureBackfillResult> RunAsync(
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1");
        }

        if (!_settings.BackfillCaptures)
        {
            _logger?.LogInformation(
                "Context Fabric: the capture backfill is disabled (ContextFabric:BackfillCaptures=false), " +
                "so Inbox reads stay on the legacy queue row.");
            return new CaptureBackfillResult(Ran: false, 0, 0, 0, Remaining: -1, Complete: false);
        }

        var state = await _backfillStore.GetStateAsync(CaptureBackfillState.LegacyQueueBackfillKey, cancellationToken)
                    ?? CaptureBackfillState.ForLegacyQueue(DateTimeOffset.UtcNow);

        var migrated = 0;
        var reconciled = 0;
        var skipped = new HashSet<Guid>();
        string? lastSkipReason = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _backfillStore.GetLegacyCaptureBacklogAsync(batchSize, skipped, cancellationToken);
            if (batch.Count == 0)
            {
                // The only loop exit: the backlog holds nothing this run has not already tried. A
                // batch that made no progress is NOT an exit -- its rows go into the exclusion set
                // and the next read steps over them to the healthy rows behind.
                break;
            }

            var outcome = await ProcessBatchAsync(batch, cancellationToken);
            migrated += outcome.Migrated;
            reconciled += outcome.Reconciled;
            foreach (var failedId in outcome.Failed)
            {
                skipped.Add(failedId);
            }

            lastSkipReason = outcome.LastSkipReason ?? lastSkipReason;

            state.RecordBatch(outcome.Migrated);
            await _backfillStore.SaveStateAsync(state, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _backfillStore.ReleaseTrackedBatchAsync(cancellationToken);
        }

        var remaining = await _backfillStore.CountLegacyCaptureBacklogAsync(cancellationToken);
        state.RecordSkipped(skipped.Count, lastSkipReason);

        if (remaining == 0)
        {
            state.MarkComplete(DateTimeOffset.UtcNow);
        }
        else
        {
            _logger?.LogError(
                "Context Fabric: {Remaining} capture queue row(s) are still outstanding after the backfill " +
                "({Skipped} of them could not be mapped; last reason: {Reason}). The marker stays incomplete, " +
                "so Inbox reads keep using the queue row for every capture until they are resolved.",
                remaining,
                skipped.Count,
                lastSkipReason ?? "none");
        }

        await _backfillStore.SaveStateAsync(state, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (migrated > 0 || reconciled > 0 || skipped.Count > 0)
        {
            _logger?.LogInformation(
                "Context Fabric: capture backfill brought in {Migrated} legacy row(s), reconciled {Reconciled}, " +
                "skipped {Skipped}, {Remaining} left outstanding. Read switch armed: {MarkerComplete}.",
                migrated,
                reconciled,
                skipped.Count,
                remaining,
                state.IsComplete);
        }

        return new CaptureBackfillResult(
            Ran: true,
            migrated,
            reconciled,
            skipped.Count,
            remaining,
            Complete: remaining == 0);
    }

    private readonly record struct BatchOutcome(
        int Migrated,
        int Reconciled,
        IReadOnlyList<Guid> Failed,
        string? LastSkipReason);

    private async Task<BatchOutcome> ProcessBatchAsync(
        IReadOnlyList<LlmRequest> batch,
        CancellationToken cancellationToken)
    {
        var migrated = 0;
        var reconciled = 0;
        var failed = new List<Guid>();
        string? lastSkipReason = null;

        foreach (var request in batch)
        {
            try
            {
                var payload = CaptureRequestContract.ParseStoredPayload(request.Payload);
                var existing = await _captureStore.GetByIdForUpdateAsync(request.Id, request.UserId, cancellationToken);

                if (existing is null)
                {
                    // The divergence join cannot tell "no capture" from "a capture owned by somebody
                    // else"; the owner-scoped read can. A cross-owner id would be a data fault, and
                    // inserting over it would fail the whole batch on a primary-key violation.
                    if (await _captureStore.ExistsAsync(request.Id, cancellationToken))
                    {
                        throw new DomainException(
                            ErrorCodes.ValidationError,
                            "A capture with this id already exists under a different owner");
                    }

                    await _captureStore.AddAsync(BuildFromLegacy(request, payload), cancellationToken);
                    migrated++;
                    continue;
                }

                await ReconcileAsync(existing, request, payload, cancellationToken);
                reconciled++;
            }
            catch (Exception ex) when (ex is DomainException or FormatException or InvalidOperationException)
            {
                failed.Add(request.Id);
                lastSkipReason = ex.GetType().Name + ": " + ex.Message;
                _logger?.LogWarning(
                    ex,
                    "Context Fabric: legacy capture {CaptureId} could not be reconciled with the durable " +
                    "aggregate; it stays readable through its queue row and the run steps over it.",
                    request.Id);
            }
        }

        return new BatchOutcome(migrated, reconciled, failed, lastSkipReason);
    }

    private static Capture BuildFromLegacy(LlmRequest request, CapturePayloadV1 payload) =>
        CaptureIntakeService.BuildCapture(
            request,
            payload,
            request.UserId,
            request.BoardId,
            legacyState: ResolveLegacyState(request, payload));

    private static CaptureLegacyState ResolveLegacyState(LlmRequest request, CapturePayloadV1 payload)
    {
        var hasLinkedProposal = payload.Provenance?.ProposalId is { } proposalId && proposalId != Guid.Empty;
        var isConverted = payload.Provenance?.ConvertedAt is not null;
        return CaptureLegacyStateMapping.Resolve(
            request.Status,
            hasLinkedProposal,
            isConverted,
            payload.Disposition?.Kind);
    }

    /// <summary>
    /// Brings an existing capture back into agreement with the queue row that moved past it. Sources
    /// are immutable, so a changed text appends a superseding asset and the original stays readable.
    /// The reconciliation stamp is recorded last and unconditionally: a capture that needed no change
    /// must still leave the backlog, or it would be re-examined on every start forever.
    /// </summary>
    private async Task ReconcileAsync(
        Capture capture,
        LlmRequest request,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken)
    {
        var legacyState = ResolveLegacyState(request, payload);

        // Archived is terminal for disposition, not evidence that source text agrees. The aggregate
        // cannot accept a superseding asset once archived, so leave a mismatch outstanding and let
        // the run's normal skip path retain queue-row fallback instead of stamping stale text away.
        if (capture.Disposition == CaptureUserDisposition.Archived &&
            !string.Equals(capture.CurrentText, payload.Text, StringComparison.Ordinal))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Cannot reconcile source text on an archived capture");
        }

        // An archived capture whose text already agrees rejects the remaining projections; only its
        // queue reconciliation stamp may move forward.
        if (capture.Disposition != CaptureUserDisposition.Archived)
        {
            if (!string.IsNullOrWhiteSpace(payload.Text) &&
                !string.Equals(capture.CurrentText, payload.Text, StringComparison.Ordinal))
            {
                capture.SupersedeInlineTextSource(payload.Text);
            }

            if (!string.IsNullOrWhiteSpace(payload.ExternalRef) &&
                !capture.ActiveSourceAssets.Any(asset =>
                    asset.StorageKind == SourceAssetStorageKind.ExternalReference &&
                    string.Equals(asset.ExternalReference, payload.ExternalRef.Trim(), StringComparison.Ordinal)))
            {
                capture.AddExternalReferenceSource(payload.ExternalRef);
            }

            capture.Retitle(payload.TitleHint);
            capture.RecordProcessingSummary(legacyState.ProcessingSummary);
            capture.RecordActionState(legacyState.ActionState);

            // Only ever forward: a durable Reactivate has no queue-row twin, so a capture the user
            // brought back must not be dragged into Kept again by a stale receipt.
            if (legacyState.Disposition == CaptureUserDisposition.Archived)
            {
                capture.Archive();
            }
            else if (legacyState.Disposition == CaptureUserDisposition.Kept &&
                     capture.Disposition == CaptureUserDisposition.Active)
            {
                capture.Keep();
            }
        }

        capture.RecordLegacyReconciliation(request.UpdatedAt);
        await _captureStore.UpdateAsync(capture, cancellationToken);
    }
}
