using Taskdeck.Application.Interfaces;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Api.Telemetry;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Workers;

/// <summary>
/// Drains Processing transcript-capture requests (<c>inbox.capture.transcript.*</c>) into
/// proposals via <see cref="ICaptureTriageService"/> — the transcript worker lane of REVIVAL-08.
/// <para>
/// A dedicated worker (rather than a third lane inside <see cref="LlmQueueToProposalWorker"/>)
/// because that worker awaits its whole batch per tick: one LLM-backed transcript triage running
/// seconds-to-minutes would stall every capture and automation item behind it. The lane split is
/// enforced at the repository (fetch and claim predicates are mutually exclusive), so the two
/// workers can never claim each other's rows.
/// </para>
/// <para>
/// Lifecycle mirrors the capture lane: items are enqueued Pending, the API's triage endpoint marks
/// them Processing, and this worker re-claims Processing rows under optimistic concurrency. Crash
/// recovery is the same self-heal the capture lane relies on — an abandoned Processing row is
/// simply re-fetched and re-claimed on a later tick, and the payload-provenance short-circuit plus
/// the triage service's existing-proposal guard keep the replay idempotent. Within one process no
/// double-claim is possible because ticks are serialized (the batch is awaited before the next
/// poll); the cross-process caveat matches the single-worker-process assumption documented in
/// <see cref="LlmQueueToProposalWorker"/>'s recovery sweep.
/// </para>
/// </summary>
public class TranscriptTriageWorker : BackgroundService
{
    private const string QueueNameTranscriptTriage = "transcript-triage";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerSettings _settings;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;
    private readonly ILogger<TranscriptTriageWorker> _logger;

    public TranscriptTriageWorker(
        IServiceScopeFactory scopeFactory,
        WorkerSettings settings,
        WorkerHeartbeatRegistry workerHeartbeatRegistry,
        ILogger<TranscriptTriageWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _workerHeartbeatRegistry = workerHeartbeatRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TranscriptTriageWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            _workerHeartbeatRegistry.ReportHeartbeat(nameof(TranscriptTriageWorker));

            if (_settings.EnableAutoQueueProcessing)
            {
                try
                {
                    await ProcessBatchAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Error in TranscriptTriageWorker iteration. {ExceptionSummary}",
                        SensitiveDataRedactor.SummarizeException(ex));
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.QueuePollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("TranscriptTriageWorker stopped");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var fetchLimit = Math.Max(1, _settings.MaxBatchSize);
        var transcriptItems = (await unitOfWork.LlmQueue.GetOldestProcessingTranscriptAsync(fetchLimit, ct))
            .OrderBy(item => item.CreatedAt)
            .ToList();

        // True backlog depth via the count query; the bounded list's Count would saturate at the
        // fetch limit and hide backlog growth (same reasoning as the sibling worker's gauges).
        var transcriptBacklog = await unitOfWork.LlmQueue.CountProcessingTranscriptAsync(ct);
        TaskdeckTelemetry.AutomationQueueBacklog.Record(
            transcriptBacklog,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.QueueName, QueueNameTranscriptTriage));

        if (transcriptItems.Count == 0)
        {
            return;
        }

        // Deliberately SEQUENTIAL (unlike the sibling worker's semaphore fan-out). Quota safety no
        // longer depends on this: the atomic reservation (#1313) serializes concurrent quota
        // boundary-crossers even across processes. The lane stays sequential as a perf/spend choice —
        // transcript triage is rare, slow work where throughput matters far less than keeping at most
        // one expensive LLM extraction in flight (bounding concurrent spend and provider pressure).
        foreach (var item in transcriptItems)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessTranscriptItemAsync(item.Id, item.UpdatedAt, ct);

            // Provider-call progress pulses cover a long map-reduce item; this keeps the worker
            // healthy across post-processing and between sequential queue items without making a
            // whole batch look alive after a later item wedges.
            _workerHeartbeatRegistry.ReportHeartbeat(nameof(TranscriptTriageWorker));
        }
    }

    private async Task ProcessTranscriptItemAsync(Guid itemId, DateTimeOffset expectedUpdatedAt, CancellationToken ct)
    {
        using var activity = TaskdeckTelemetry.ActivitySource.StartActivity(
            "taskdeck.worker.process_transcript_triage_item",
            System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag(TaskdeckTelemetryTags.WorkerName, nameof(TranscriptTriageWorker));
        activity?.SetTag(TaskdeckTelemetryTags.LlmRequestId, itemId.ToString());

        var stopWatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "skipped";

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var triageService = scope.ServiceProvider.GetRequiredService<ICaptureTriageService>();
        var transcriptRepository = scope.ServiceProvider.GetRequiredService<ITranscriptRepository>();

        var claimed = await unitOfWork.LlmQueue.TryClaimProcessingTranscriptAsync(itemId, expectedUpdatedAt, ct);
        if (!claimed)
        {
            outcome = "already_claimed";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
            return;
        }

        var item = await unitOfWork.LlmQueue.GetByIdAsync(itemId, ct);
        if (item == null ||
            item.Status != RequestStatus.Processing ||
            !CaptureRequestContract.IsTranscriptRequestType(item.RequestType))
        {
            outcome = "already_claimed";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
            return;
        }

        activity?.SetTag(TaskdeckTelemetryTags.RequestType, item.RequestType);
        activity?.SetTag(TaskdeckTelemetryTags.UserId, item.UserId.ToString());
        if (item.BoardId.HasValue)
        {
            activity?.SetTag(TaskdeckTelemetryTags.BoardId, item.BoardId.Value.ToString());
        }

        try
        {
            var parsedPayloadResult = CaptureRequestContract.ParsePayload(item.Payload, allowServerAttributionFields: true);
            if (!parsedPayloadResult.IsSuccess)
            {
                var scheduledForRetry = await HandleFailureWithRetryAsync(
                    unitOfWork,
                    item,
                    parsedPayloadResult.ErrorCode,
                    parsedPayloadResult.ErrorMessage ?? "Invalid capture payload",
                    ct);

                outcome = scheduledForRetry ? "failed_retry" : "failed_permanent";
                stopWatch.Stop();
                RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
                return;
            }

            if (parsedPayloadResult.Value.Provenance?.ProposalId is { } existingProposalId &&
                existingProposalId != Guid.Empty)
            {
                item.MarkAsCompleted();
                await unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Transcript capture item {ItemId} already linked to proposal {ProposalId}; marking completed",
                    item.Id,
                    existingProposalId);

                outcome = "completed_existing";
                stopWatch.Stop();
                RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
                return;
            }

            var transcript = item.TranscriptId.HasValue
                ? await transcriptRepository.GetByIdForUserAsync(item.TranscriptId.Value, item.UserId, ct)
                : null;

            if (item.TranscriptId.HasValue && transcript is null)
            {
                var linkedTranscriptRetryScheduled = await HandleFailureWithRetryAsync(
                    unitOfWork,
                    item,
                    ErrorCodes.NotFound,
                    "The linked transcript could not be found",
                    ct);

                outcome = linkedTranscriptRetryScheduled ? "failed_retry" : "failed_permanent";
                stopWatch.Stop();
                RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
                return;
            }

            if (transcript is null)
            {
                transcript = new Transcript(
                    item.UserId,
                    parsedPayloadResult.Value.Source,
                    parsedPayloadResult.Value.Text,
                    boardId: item.BoardId,
                    createdFromCaptureId: item.Id);
                await transcriptRepository.AddAsync(transcript, ct);
                item.AttachTranscript(transcript.Id);

                // Persist the transcript and queue linkage together before any provider work. A
                // replay observes TranscriptId and reuses this canonical text instead of creating
                // a second transcript.
                await unitOfWork.SaveChangesAsync(ct);
            }

            var canonicalPayload = parsedPayloadResult.Value with { Text = transcript.Text };

            var triageResult = await triageService.CreateProposalFromTranscriptAsync(
                item.Id,
                item.UserId,
                item.BoardId,
                transcript.Id,
                canonicalPayload,
                ct);

            if (triageResult.IsSuccess)
            {
                var linkedPayload = CaptureRequestContract.WithProvenance(
                    parsedPayloadResult.Value,
                    item.Id,
                    triageRunId: triageResult.Value.TriageRunId,
                    proposalId: triageResult.Value.ProposalId,
                    promptVersion: triageResult.Value.PromptVersion,
                    provider: CaptureRequestContract.SanitizeProvenanceMetadata(
                        triageResult.Value.Provider,
                        CaptureRequestContract.MaxProviderLength),
                    model: CaptureRequestContract.SanitizeProvenanceMetadata(
                        triageResult.Value.Model,
                        CaptureRequestContract.MaxModelLength));

                item.UpdatePayload(CaptureRequestContract.SerializePayload(linkedPayload));
                item.MarkAsCompleted();
                await unitOfWork.SaveChangesAsync(ct);

                // A null ProposalId is the "triaged, nothing to propose" verdict: the item is
                // Completed without a linked proposal (capture status: Triaged), never Failed —
                // a correct empty extraction is a successful triage, not an error.
                if (triageResult.Value.ProposalId is null)
                {
                    _logger.LogInformation(
                        "Transcript capture item {ItemId} triaged by {Provider}/{Model}: no actionable items; completed without a proposal",
                        item.Id,
                        triageResult.Value.Provider,
                        triageResult.Value.Model);
                    outcome = "completed_empty";
                }
                else
                {
                    _logger.LogInformation(
                        "Transcript capture item {ItemId} triaged into proposal {ProposalId} by {Provider}/{Model}",
                        item.Id,
                        triageResult.Value.ProposalId,
                        triageResult.Value.Provider,
                        triageResult.Value.Model);
                    outcome = "completed";
                }

                stopWatch.Stop();
                RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
                return;
            }

            var retryScheduled = await HandleFailureWithRetryAsync(
                unitOfWork,
                item,
                triageResult.ErrorCode,
                triageResult.ErrorMessage ?? "Transcript triage failed",
                ct);

            outcome = retryScheduled ? "failed_retry" : "failed_permanent";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Unhandled exception triaging transcript queue item {ItemId}. {ExceptionSummary}",
                item.Id,
                SensitiveDataRedactor.SummarizeException(ex));
            var scheduledForRetry = await HandleFailureWithRetryAsync(
                unitOfWork,
                item,
                ErrorCodes.UnexpectedError,
                ex.Message,
                ct);

            outcome = scheduledForRetry ? "failed_retry" : "failed_unhandled";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
        }
    }

    /// <summary>
    /// Mirrors <see cref="LlmQueueToProposalWorker"/>'s failure-retry helper with
    /// <c>retryAsProcessing</c> fixed to true: transcript items — like all capture items — are only
    /// ever read from Processing, so a retry left in Pending would strand forever. The retry
    /// classification is intentionally identical (null/UnexpectedError/Conflict are transient);
    /// LLM-availability problems never reach this path because the triage service degrades to the
    /// deterministic extractor in-process instead of failing the item.
    /// </summary>
    private async Task<bool> HandleFailureWithRetryAsync(
        IUnitOfWork unitOfWork,
        Taskdeck.Domain.Entities.LlmRequest item,
        string? errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        var safeErrorMessage = SensitiveDataRedactor.SanitizeLlmFailureMessage(errorCode, errorMessage);
        var currentRetryCount = item.RetryCount;
        var shouldRetry = IsTransientFailure(errorCode) && currentRetryCount + 1 < _settings.MaxRetries;

        item.MarkAsFailed(safeErrorMessage);
        await unitOfWork.SaveChangesAsync(ct);

        if (!shouldRetry)
        {
            _logger.LogWarning(
                "Transcript queue item {ItemId} failed permanently after {RetryCount} retries with error code {ErrorCode}: {ErrorMessage}",
                item.Id,
                item.RetryCount,
                errorCode,
                safeErrorMessage);
            return false;
        }

        var backoff = TimeSpan.FromSeconds(GetRetryBackoffSeconds(item.RetryCount));
        if (backoff > TimeSpan.Zero)
        {
            await Task.Delay(backoff, ct);
        }

        item.ResetForRetry();
        item.MarkAsProcessing();
        await unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Transcript queue item {ItemId} scheduled for retry attempt {RetryCount}",
            item.Id,
            item.RetryCount + 1);
        return true;
    }

    private bool IsTransientFailure(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return true;
        }

        return errorCode == ErrorCodes.UnexpectedError ||
               errorCode == ErrorCodes.Conflict;
    }

    private int GetRetryBackoffSeconds(int retryCount)
    {
        if (_settings.RetryBackoffSeconds.Length == 0)
        {
            return 0;
        }

        var index = Math.Clamp(retryCount - 1, 0, _settings.RetryBackoffSeconds.Length - 1);
        return _settings.RetryBackoffSeconds[index];
    }

    private static void RecordWorkerProcessingMetrics(double durationMs, string outcome)
    {
        TaskdeckTelemetry.WorkerItemProcessingDurationMs.Record(
            durationMs,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(TranscriptTriageWorker)),
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, outcome));

        TaskdeckTelemetry.WorkerItemsProcessed.Add(
            1,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(TranscriptTriageWorker)),
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, outcome));
    }
}
