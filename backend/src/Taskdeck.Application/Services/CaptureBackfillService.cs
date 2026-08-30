using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>The outcome of one <see cref="CaptureBackfillService.RunAsync"/> call.</summary>
public readonly record struct CaptureBackfillResult(
    bool Ran,
    int Migrated,
    int Skipped,
    int Remaining,
    bool Complete);

/// <summary>
/// The ID-preserving backfill of every capture-shaped <see cref="LlmRequest"/> row into the durable
/// <see cref="Capture"/> aggregate (ADR-0065 Decision 1, ruling 7; CF-01 <c>#2255</c>).
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
/// <b>Idempotent and resumable</b> by construction: the backlog is an anti-join (capture-shaped
/// queue rows with no <see cref="Capture"/> under the same id), so a committed row leaves the
/// backlog forever, re-running creates nothing twice, and a crash mid-way resumes at the next
/// uncommitted row. Each batch commits in its own unit of work together with the progress marker.
/// </para>
/// <para>
/// <b>A row that cannot be mirrored is never fatal.</b> It is counted and logged by id (never by
/// content) and left in the backlog; the Inbox read path falls back to that row's queue payload, so
/// it stays visible. When a whole batch fails to make progress the run stops rather than spinning,
/// and the marker still completes so the read switch is not held hostage by one bad row.
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
    /// Drains the backlog and records completion. Safe to call on every startup: an already-complete
    /// marker with an empty backlog costs one indexed count.
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
            return new CaptureBackfillResult(Ran: false, 0, 0, Remaining: -1, Complete: false);
        }

        var state = await _backfillStore.GetStateAsync(CaptureBackfillState.LegacyQueueBackfillKey, cancellationToken)
                    ?? CaptureBackfillState.ForLegacyQueue(DateTimeOffset.UtcNow);

        var migrated = 0;
        var skipped = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _backfillStore.GetLegacyCaptureBacklogAsync(batchSize, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            var (batchMigrated, batchSkipped, lastSkipReason) = await MigrateBatchAsync(batch, cancellationToken);
            migrated += batchMigrated;
            skipped += batchSkipped;

            state.RecordBatch(batchMigrated, batchSkipped, lastSkipReason);
            await _backfillStore.SaveStateAsync(state, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (batchMigrated == 0)
            {
                // Nothing in this batch could be migrated, so the backlog can no longer shrink. Stop
                // rather than loop forever; the remaining rows keep their queue-row fallback.
                _logger?.LogWarning(
                    "Context Fabric: {BatchSize} legacy capture row(s) could not be migrated into the durable " +
                    "aggregate and keep being read from their queue row. Last reason: {Reason}",
                    batch.Count,
                    lastSkipReason ?? "unknown");
                break;
            }
        }

        var remaining = await _backfillStore.CountLegacyCaptureBacklogAsync(cancellationToken);
        state.MarkComplete(DateTimeOffset.UtcNow);
        await _backfillStore.SaveStateAsync(state, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (migrated > 0 || skipped > 0)
        {
            _logger?.LogInformation(
                "Context Fabric: capture backfill migrated {Migrated} legacy row(s), skipped {Skipped}, " +
                "{Remaining} left in the backlog. Inbox reads resolve through ICaptureStore.",
                migrated,
                skipped,
                remaining);
        }

        return new CaptureBackfillResult(Ran: true, migrated, skipped, remaining, Complete: state.IsComplete);
    }

    private async Task<(int Migrated, int Skipped, string? LastSkipReason)> MigrateBatchAsync(
        IReadOnlyList<LlmRequest> batch,
        CancellationToken cancellationToken)
    {
        var migrated = 0;
        var skipped = 0;
        string? lastSkipReason = null;

        foreach (var request in batch)
        {
            try
            {
                var payload = CaptureRequestContract.ParseStoredPayload(request.Payload);
                var hasLinkedProposal = payload.Provenance?.ProposalId is { } proposalId && proposalId != Guid.Empty;
                var isConverted = payload.Provenance?.ConvertedAt is not null;
                var legacyState = CaptureLegacyStateMapping.Resolve(
                    request.Status,
                    hasLinkedProposal,
                    isConverted,
                    payload.Disposition?.Kind);

                var capture = CaptureIntakeService.BuildCapture(
                    request,
                    payload,
                    request.UserId,
                    request.BoardId,
                    legacyState: legacyState);

                await _captureStore.AddAsync(capture, cancellationToken);
                migrated++;
            }
            catch (Exception ex) when (ex is DomainException or FormatException or InvalidOperationException)
            {
                skipped++;
                lastSkipReason = ex.GetType().Name + ": " + ex.Message;
                _logger?.LogWarning(
                    ex,
                    "Context Fabric: legacy capture {CaptureId} could not be migrated into the durable " +
                    "aggregate; it stays readable through its queue row.",
                    request.Id);
            }
        }

        return (migrated, skipped, lastSkipReason);
    }
}
