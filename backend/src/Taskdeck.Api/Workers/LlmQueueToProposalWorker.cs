using Taskdeck.Application.Interfaces;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Api.Telemetry;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Workers;

public class LlmQueueToProposalWorker : BackgroundService
{
    private const string QueueNameLlm = "llm";
    private const string QueueNameCaptureTriage = "capture-triage";

    // Floor on the stuck-recovery lease so an aggressively-low ProcessingLeaseSeconds can never sweep a
    // request that was claimed seconds ago and is still legitimately in flight. Mirrors the floor the
    // OutboundWebhookDeliveryWorker recovery sweep uses.
    private const int MinStuckRecoveryLeaseSeconds = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerSettings _settings;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;
    private readonly ILogger<LlmQueueToProposalWorker> _logger;

    public LlmQueueToProposalWorker(
        IServiceScopeFactory scopeFactory,
        WorkerSettings settings,
        WorkerHeartbeatRegistry workerHeartbeatRegistry,
        ILogger<LlmQueueToProposalWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _workerHeartbeatRegistry = workerHeartbeatRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LlmQueueToProposalWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            _workerHeartbeatRegistry.ReportHeartbeat(nameof(LlmQueueToProposalWorker));

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
                        "Error in LlmQueueToProposalWorker iteration. {ExceptionSummary}",
                        SensitiveDataRedactor.SummarizeException(ex));
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.QueuePollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("LlmQueueToProposalWorker stopped");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        // Reclaim non-capture requests abandoned in Processing by a crashed worker before draining the
        // queue (#1209), so a recovered item becomes eligible again within this same tick. Capture-triage
        // items self-heal via the Processing re-claim path below, so the sweep targets non-capture work
        // only -- the one kind read solely from Pending and otherwise stuck forever. The sweep is isolated
        // in its own try/catch so a recovery hiccup (e.g. a transient SaveChanges failure) can never starve
        // the tick's normal queue draining; the failure is logged and retried on the next poll.
        try
        {
            await RecoverStuckProcessingItemsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Stuck-Processing recovery sweep failed; continuing with normal queue draining. {ExceptionSummary}",
                SensitiveDataRedactor.SummarizeException(ex));
        }

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        // Bound each read at the database to the work one tick can consume (BuildFairBatchItems emits at
        // most MaxBatchSize items total, so it never needs more than MaxBatchSize of either kind). The
        // capture/non-capture predicate is applied in-query by these methods, so the bound never fills with
        // rows the worker would discard -- bounding raw status before an in-memory type filter could let
        // older untriaged capture rows starve non-capture automation work (and vice-versa on Processing).
        var fetchLimit = Math.Max(1, _settings.MaxBatchSize);
        var pendingItems = (await unitOfWork.LlmQueue.GetOldestPendingNonCaptureAsync(fetchLimit, ct))
            .OrderBy(item => item.CreatedAt)
            .ToList();
        var triagingCaptureItems = (await unitOfWork.LlmQueue.GetOldestProcessingCaptureAsync(fetchLimit, ct))
            .OrderBy(item => item.CreatedAt)
            .ToList();

        // Backlog gauges report TRUE depth via count queries; recording the bounded lists' Count would
        // saturate the gauge at the fetch limit and hide backlog growth -- the exact signal operators need.
        var pendingBacklog = await unitOfWork.LlmQueue.CountPendingNonCaptureAsync(ct);
        var captureBacklog = await unitOfWork.LlmQueue.CountProcessingCaptureAsync(ct);
        TaskdeckTelemetry.AutomationQueueBacklog.Record(
            pendingBacklog,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.QueueName, QueueNameLlm));
        TaskdeckTelemetry.AutomationQueueBacklog.Record(
            captureBacklog,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.QueueName, QueueNameCaptureTriage));

        var workBatch = BuildFairBatchItems(triagingCaptureItems, pendingItems, _settings.MaxBatchSize);

        if (workBatch.Count == 0)
        {
            return;
        }

        var maxConcurrency = Math.Clamp(_settings.MaxConcurrency, 1, _settings.MaxBatchSize);
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task>(workBatch.Count);

        foreach (var batchItem in workBatch)
        {
            await semaphore.WaitAsync(ct);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (batchItem.IsCaptureTriage)
                    {
                        if (!batchItem.ExpectedUpdatedAt.HasValue)
                        {
                            return;
                        }

                        await ProcessCaptureTriageItemAsync(batchItem.ItemId, batchItem.ExpectedUpdatedAt.Value, ct);
                    }
                    else
                    {
                        if (!batchItem.ExpectedUpdatedAt.HasValue)
                        {
                            return;
                        }

                        await ProcessSingleItemAsync(batchItem.ItemId, batchItem.ExpectedUpdatedAt.Value, ct);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }

    private async Task RecoverStuckProcessingItemsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // A non-capture request that has sat in Processing longer than the processing lease is treated as
        // abandoned: the worker that claimed it (stamping UpdatedAt to now) never reached a terminal
        // transition. Re-claiming a live, just-claimed request is prevented because its UpdatedAt is fresh,
        // and because ProcessBatchAsync runs this sweep before -- and is awaited sequentially with -- the
        // batch drain, so a single worker process can never sweep its own in-flight work. The sweep assumes
        // a single worker process (the local-first deployment): like the webhook recovery sweep it has no
        // optimistic-concurrency guard, so two processes against one DB could double-recover a row.
        var leaseSeconds = Math.Max(MinStuckRecoveryLeaseSeconds, _settings.ProcessingLeaseSeconds);
        var staleBefore = DateTimeOffset.UtcNow.AddSeconds(-leaseSeconds);
        var fetchLimit = Math.Max(1, _settings.MaxBatchSize);

        var stuckItems = await unitOfWork.LlmQueue.GetStuckProcessingNonCaptureAsync(staleBefore, fetchLimit, ct);
        if (stuckItems.Count == 0)
        {
            return;
        }

        var completed = 0;
        var requeued = 0;
        var failedPermanently = 0;
        foreach (var item in stuckItems)
        {
            // If the crashed attempt already committed this request's proposal, the work actually succeeded
            // -- only the completing status flip was lost. Complete it regardless of the retry budget,
            // rather than requeueing (which the drain's guard would complete anyway, after a wasted cycle)
            // or, on the last attempt, failing it -- which would mislabel a request that DID produce its
            // proposal as Failed and never route it through the drain's completion guard.
            var existingProposal = await unitOfWork.AutomationProposals.GetBySourceReferenceAsync(
                ProposalSourceType.Queue, item.Id.ToString(), ct);
            if (existingProposal != null)
            {
                item.MarkAsCompleted();
                completed++;
                continue;
            }

            // Recovery counts against the retry budget so a request that repeatedly crashes the worker
            // eventually fails permanently instead of looping forever. MarkAsFailed (Processing -> Failed,
            // RetryCount++) then ResetForRetry (Failed -> Pending) reuses the same transitions and the same
            // pre-increment budget check (RetryCount + 1 < MaxRetries) as HandleFailureWithRetryAsync. It
            // deliberately omits that path's IsTransientFailure gate: a stale-Processing row means the
            // worker crashed mid-flight (no error code to classify), which we always treat as retryable.
            // The lease itself spaces successive recoveries of a poison-pill payload >= the lease apart, so
            // no explicit retry backoff is needed here.
            var budgetRemains = item.RetryCount + 1 < _settings.MaxRetries;
            item.MarkAsFailed("Recovered from a stale Processing state; the worker that claimed this request did not complete it.");
            if (budgetRemains)
            {
                item.ResetForRetry();
                requeued++;
            }
            else
            {
                failedPermanently++;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Recovered {StuckCount} non-capture queue item(s) stuck in Processing beyond {LeaseSeconds}s: {CompletedCount} already had a proposal and were completed, {RequeuedCount} returned to Pending, {FailedCount} failed (retry budget exhausted).",
            stuckItems.Count,
            leaseSeconds,
            completed,
            requeued,
            failedPermanently);

        if (completed > 0)
        {
            RecordRecoveryMetrics(completed, "recovered_stuck_completed");
        }
        if (requeued > 0)
        {
            RecordRecoveryMetrics(requeued, "recovered_stuck_requeued");
        }
        if (failedPermanently > 0)
        {
            RecordRecoveryMetrics(failedPermanently, "recovered_stuck_failed");
        }
    }

    private static void RecordRecoveryMetrics(int count, string outcome)
    {
        TaskdeckTelemetry.WorkerItemsProcessed.Add(
            count,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(LlmQueueToProposalWorker)),
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, outcome));
    }

    private async Task ProcessSingleItemAsync(Guid itemId, DateTimeOffset expectedUpdatedAt, CancellationToken ct)
    {
        using var activity = TaskdeckTelemetry.ActivitySource.StartActivity(
            "taskdeck.worker.process_llm_queue_item",
            System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag(TaskdeckTelemetryTags.WorkerName, nameof(LlmQueueToProposalWorker));
        activity?.SetTag(TaskdeckTelemetryTags.LlmRequestId, itemId.ToString());

        var stopWatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "skipped";

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var planner = scope.ServiceProvider.GetRequiredService<IAutomationPlannerService>();

        var claimed = await unitOfWork.LlmQueue.TryClaimProcessingAsync(itemId, expectedUpdatedAt, ct);
        if (!claimed)
        {
            outcome = "already_claimed";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
            return;
        }

        var item = await unitOfWork.LlmQueue.GetByIdAsync(itemId, ct);
        if (item == null || item.Status != RequestStatus.Processing)
        {
            // We successfully claimed the row (UPDATE flipped it to Processing) but the
            // post-claim re-fetch no longer sees it Processing. The row vanished or was
            // mutated between our UPDATE and SELECT -- this is distinct from losing the
            // claim race ("already_claimed"), so surface it with its own outcome and a
            // warning so the orphaned-Processing case stays visible in telemetry/logs.
            _logger.LogWarning(
                "Queue item {ItemId} claimed but re-fetch returned {Status}; row vanished or mutated between claim and read",
                itemId,
                item?.Status.ToString() ?? "null");
            outcome = "claimed_then_missing";
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

        // Idempotency guard (#1209 review): a prior attempt may have created and committed the proposal,
        // then crashed (or failed mid-save and been requeued) before this request was marked completed.
        // Re-running the planner would create a DUPLICATE PendingReview proposal (proposal creation is not
        // idempotent on SourceReferenceId), so if a Queue-sourced proposal already exists for this request,
        // complete it instead of reprocessing. Mirrors the capture path's existing-proposal short-circuit.
        var existingProposal = await unitOfWork.AutomationProposals.GetBySourceReferenceAsync(
            ProposalSourceType.Queue, item.Id.ToString(), ct);
        if (existingProposal != null)
        {
            item.MarkAsCompleted();
            await unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Queue item {ItemId} already linked to proposal {ProposalId}; marking completed without reprocessing",
                item.Id,
                existingProposal.Id);
            outcome = "completed_existing";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
            return;
        }

        try
        {
            var proposalResult = await planner.ParseInstructionAsync(
                item.Payload,
                item.UserId,
                item.BoardId,
                ct,
                sourceType: ProposalSourceType.Queue,
                sourceReferenceId: item.Id.ToString(),
                correlationId: item.Id.ToString());

            if (proposalResult.IsSuccess)
            {
                item.MarkAsCompleted();
                await unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Queue item {ItemId} processed successfully", item.Id);
                outcome = "completed";
                stopWatch.Stop();
                RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
                return;
            }

            var scheduledForRetry = await HandleFailureWithRetryAsync(
                unitOfWork,
                item,
                proposalResult.ErrorCode,
                proposalResult.ErrorMessage ?? "Unknown error",
                ct);

            outcome = scheduledForRetry ? "failed_retry" : "failed_permanent";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
        }
        // Shutdown or caller cancellation is not a processing failure. Without this the
        // planner's rethrow is caught below and converted straight back into an
        // UnexpectedError, which IsTransientFailure treats as retryable — so the item would be
        // marked Failed and requeued purely because the host was stopping.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseClaimOnShutdownAsync(item);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Unhandled exception processing queue item {ItemId}. {ExceptionSummary}",
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
    /// Returns a claim abandoned by a graceful shutdown to Pending so the stop does not cost the request
    /// a retry (#1605). Left in Processing, the row is indistinguishable from a crashed attempt: the next
    /// run's <see cref="RecoverStuckProcessingItemsAsync"/> finds no proposal for it and charges the
    /// budget (MarkAsFailed, RetryCount++), so enough restarts fail a healthy request permanently at
    /// MaxRetries — and it waits out the processing lease before retrying either way. Releasing it
    /// charges nothing and makes it eligible again on the very next tick. A genuine crash never reaches
    /// this path, so the sweep's retry accounting (the poison-pill guard) is untouched.
    /// </summary>
    /// <remarks>
    /// Non-capture (proposal) lane only. Capture-triage and transcript rows are READ from Processing, so
    /// Processing already is their queued state and returning one to Pending would drop it back to
    /// "untriaged in the inbox" — the wrong state, and one no worker drains.
    /// </remarks>
    private async Task ReleaseClaimOnShutdownAsync(LlmRequest item)
    {
        // The failure path may already have moved the row on (Failed, or Pending/Processing once
        // HandleFailureWithRetryAsync completed its own interrupted transition); only a still-claimed row
        // is ours to release.
        if (item.Status != RequestStatus.Processing)
        {
            return;
        }

        try
        {
            // Use a fresh scope so this shutdown cleanup cannot flush proposal or other state tracked
            // by the planner's scope. Reloading also preserves the domain transition's status guard
            // without duplicating it in a targeted SQL update.
            using var scope = _scopeFactory.CreateScope();
            var shutdownUnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var shutdownItem = await shutdownUnitOfWork.LlmQueue.GetByIdAsync(item.Id, CancellationToken.None);
            if (shutdownItem == null || shutdownItem.Status != RequestStatus.Processing)
            {
                return;
            }

            shutdownItem.ReleaseClaim();
            // CancellationToken.None: ct is already cancelled, and forwarding it would throw before the
            // release could commit — leaving exactly the stale-Processing row this exists to avoid. Same
            // deliberate shutdown-write idiom as AgentRuntime and OutboundWebhookDeliveryWorker's own
            // ReturnToPending. The fresh scope's DbContext tracks only this cleanup read, so unrelated
            // proposal state from the planner scope is not included in this flush.
            await shutdownUnitOfWork.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation(
                "Queue item {ItemId} released back to Pending during shutdown; no retry charged",
                item.Id);
        }
        catch (Exception ex)
        {
            // Never let the release write mask the cancellation: log and return so the
            // OperationCanceledException still propagates and the worker still stops promptly. The row
            // then simply stays Processing and the recovery sweep handles it as it did before #1605.
            _logger.LogWarning(
                "Failed to release queue item {ItemId} during shutdown; it stays Processing for the recovery sweep. {ExceptionSummary}",
                item.Id,
                SensitiveDataRedactor.SummarizeException(ex));
        }
    }

    private async Task ProcessCaptureTriageItemAsync(Guid itemId, DateTimeOffset expectedUpdatedAt, CancellationToken ct)
    {
        using var activity = TaskdeckTelemetry.ActivitySource.StartActivity(
            "taskdeck.worker.process_capture_triage_item",
            System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag(TaskdeckTelemetryTags.WorkerName, nameof(LlmQueueToProposalWorker));
        activity?.SetTag(TaskdeckTelemetryTags.LlmRequestId, itemId.ToString());

        var stopWatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "skipped";

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var triageService = scope.ServiceProvider.GetRequiredService<ICaptureTriageService>();

        var claimed = await unitOfWork.LlmQueue.TryClaimProcessingCaptureAsync(itemId, expectedUpdatedAt, ct);
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
            !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
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
                    ct,
                    retryAsProcessing: true);

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
                    "Capture item {ItemId} already linked to proposal {ProposalId}; marking completed",
                    item.Id,
                    existingProposalId);

                outcome = "completed_existing";
                stopWatch.Stop();
                RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
                return;
            }

            var triageResult = await triageService.CreateProposalFromCaptureAsync(
                item.Id,
                item.UserId,
                item.BoardId,
                parsedPayloadResult.Value,
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

                // A null ProposalId is the "triaged, nothing to propose" verdict (only reachable
                // here for legacy transcript-typed rows whose LLM leg ran): Completed without a
                // linked proposal renders as Triaged, never Failed.
                if (triageResult.Value.ProposalId is null)
                {
                    _logger.LogInformation(
                        "Capture item {ItemId} triaged: no actionable items; completed without a proposal",
                        item.Id);
                    outcome = "completed_empty";
                }
                else
                {
                    _logger.LogInformation(
                        "Capture item {ItemId} triaged into proposal {ProposalId}",
                        item.Id,
                        triageResult.Value.ProposalId);
                    outcome = "completed";
                }

                stopWatch.Stop();
                RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
                return;
            }

            var retryScheduled = await HandleFailureWithRetryInFreshScopeAsync(
                item.Id,
                triageResult.ErrorCode,
                triageResult.ErrorMessage ?? "Capture triage failed",
                ct,
                retryAsProcessing: true);

            outcome = retryScheduled ? "failed_retry" : "failed_permanent";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
        }
        // Same discrimination as the proposal lane. CaptureTriageService already rethrows
        // caller cancellation rather than returning an outcome, so without this guard the
        // worker converts that rethrow straight back into a retryable UnexpectedError.
        // No ReleaseClaimOnShutdownAsync here, deliberately: capture rows are read from
        // Processing, so an abandoned capture claim is already in its queued state and the next
        // tick re-claims it with no retry charged. Returning it to Pending would instead mean
        // "untriaged in the inbox", which no worker drains (see that method's remarks).
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Unhandled exception triaging capture queue item {ItemId}. {ExceptionSummary}",
                item.Id,
                SensitiveDataRedactor.SummarizeException(ex));
            var scheduledForRetry = await HandleFailureWithRetryInFreshScopeAsync(
                item.Id,
                ErrorCodes.UnexpectedError,
                ex.Message,
                ct,
                retryAsProcessing: true);

            outcome = scheduledForRetry ? "failed_retry" : "failed_unhandled";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
        }
    }

    private static List<WorkerBatchItem> BuildFairBatchItems(
        IReadOnlyList<LlmRequest> triagingCaptureItems,
        IReadOnlyList<LlmRequest> pendingItems,
        int maxBatchSize)
    {
        var batch = new List<WorkerBatchItem>(Math.Max(maxBatchSize, 0));
        if (maxBatchSize <= 0)
        {
            return batch;
        }

        var captureIndex = 0;
        var pendingIndex = 0;
        var takeCaptureFirst = triagingCaptureItems.Count > 0 &&
                               (pendingItems.Count == 0 || triagingCaptureItems[0].CreatedAt <= pendingItems[0].CreatedAt);

        while (batch.Count < maxBatchSize &&
               (captureIndex < triagingCaptureItems.Count || pendingIndex < pendingItems.Count))
        {
            if (takeCaptureFirst)
            {
                if (captureIndex < triagingCaptureItems.Count)
                {
                    var capture = triagingCaptureItems[captureIndex++];
                    batch.Add(new WorkerBatchItem(capture.Id, IsCaptureTriage: true, ExpectedUpdatedAt: capture.UpdatedAt));
                    if (batch.Count == maxBatchSize)
                    {
                        break;
                    }
                }

                if (pendingIndex < pendingItems.Count)
                {
                    var pending = pendingItems[pendingIndex++];
                    batch.Add(new WorkerBatchItem(pending.Id, IsCaptureTriage: false, ExpectedUpdatedAt: pending.UpdatedAt));
                }
            }
            else
            {
                if (pendingIndex < pendingItems.Count)
                {
                    var pending = pendingItems[pendingIndex++];
                    batch.Add(new WorkerBatchItem(pending.Id, IsCaptureTriage: false, ExpectedUpdatedAt: pending.UpdatedAt));
                    if (batch.Count == maxBatchSize)
                    {
                        break;
                    }
                }

                if (captureIndex < triagingCaptureItems.Count)
                {
                    var capture = triagingCaptureItems[captureIndex++];
                    batch.Add(new WorkerBatchItem(capture.Id, IsCaptureTriage: true, ExpectedUpdatedAt: capture.UpdatedAt));
                }
            }
        }

        return batch;
    }

    private async Task<bool> HandleFailureWithRetryAsync(
        IUnitOfWork unitOfWork,
        Taskdeck.Domain.Entities.LlmRequest item,
        string? errorCode,
        string errorMessage,
        CancellationToken ct,
        bool retryAsProcessing = false)
    {
        var safeErrorMessage = SensitiveDataRedactor.SanitizeLlmFailureMessage(errorCode, errorMessage);
        var currentRetryCount = item.RetryCount;
        var shouldRetry = IsTransientFailure(errorCode) && currentRetryCount + 1 < _settings.MaxRetries;

        item.MarkAsFailed(safeErrorMessage);
        await unitOfWork.SaveChangesAsync(ct);

        if (!shouldRetry)
        {
            _logger.LogWarning(
                "Queue item {ItemId} failed permanently after {RetryCount} retries with error code {ErrorCode}: {ErrorMessage}",
                item.Id,
                item.RetryCount,
                errorCode,
                safeErrorMessage);
            return false;
        }

        var backoff = TimeSpan.FromSeconds(GetRetryBackoffSeconds(item.RetryCount));
        try
        {
            if (backoff > TimeSpan.Zero)
            {
                await Task.Delay(backoff, ct);
            }

            ApplyRetryTransition(item, retryAsProcessing);
            await unitOfWork.SaveChangesAsync(ct);
        }
        // The Failed row above is ALREADY COMMITTED, so abandoning the requeue here strands it forever
        // (#1605): the recovery sweep only scans Processing (GetStuckProcessingNonCaptureAsync), and
        // nothing else re-enqueues a Failed row. Cancellation can land either in the backoff wait or in
        // the write itself, so both are inside this guard. Finish the transition with
        // CancellationToken.None -- the retry was already decided and the budget already charged above;
        // only the waiting is being cut short, and the shutdown must not change the outcome. The
        // completion below uses a fresh scope so it cannot flush unrelated state from the planner scope.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try
            {
                await CompleteRetryTransitionOnShutdownAsync(item.Id, retryAsProcessing);
                _logger.LogInformation(
                    "Queue item {ItemId} requeued for retry attempt {RetryCount} during shutdown",
                    item.Id,
                    item.RetryCount + 1);
            }
            catch (Exception ex)
            {
                // Same reasoning as ReleaseClaimOnShutdownAsync: never mask the cancellation. The row is
                // left Failed, which is the pre-#1605 outcome for this window.
                _logger.LogWarning(
                    "Failed to requeue queue item {ItemId} during shutdown; it stays Failed. {ExceptionSummary}",
                    item.Id,
                    SensitiveDataRedactor.SummarizeException(ex));
            }

            throw;
        }

        _logger.LogInformation(
            "Queue item {ItemId} scheduled for retry attempt {RetryCount}",
            item.Id,
            item.RetryCount + 1);
        return true;
    }

    /// <summary>
    /// Records capture-triage failures in a fresh scope. A failed proposal/revision save can leave
    /// stale Modified/Added entries in the processing DbContext; reusing it would retry those writes
    /// and prevent the queue item's failure/retry transition from committing.
    /// </summary>
    private async Task<bool> HandleFailureWithRetryInFreshScopeAsync(
        Guid itemId,
        string? errorCode,
        string errorMessage,
        CancellationToken ct,
        bool retryAsProcessing)
    {
        using var failureScope = _scopeFactory.CreateScope();
        var failureUnitOfWork = failureScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var failureItem = await failureUnitOfWork.LlmQueue.GetByIdAsync(itemId, ct);
        if (failureItem == null || failureItem.Status != RequestStatus.Processing)
        {
            _logger.LogWarning(
                "Capture queue item {ItemId} could not record triage failure because its current status is {Status}",
                itemId,
                failureItem?.Status.ToString() ?? "missing");
            return false;
        }

        return await HandleFailureWithRetryAsync(
            failureUnitOfWork,
            failureItem,
            errorCode,
            errorMessage,
            ct,
            retryAsProcessing);
    }

    private async Task CompleteRetryTransitionOnShutdownAsync(Guid itemId, bool retryAsProcessing)
    {
        using var scope = _scopeFactory.CreateScope();
        var shutdownUnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var shutdownItem = await shutdownUnitOfWork.LlmQueue.GetByIdAsync(itemId, CancellationToken.None);
        if (shutdownItem == null)
        {
            return;
        }

        ApplyRetryTransition(shutdownItem, retryAsProcessing);
        await shutdownUnitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Moves a just-failed request into its retry state: back to Pending, and on to Processing for the
    /// capture lane, which reads its queue from Processing. Written to be safe to call twice — the
    /// shutdown path re-runs it after a write that may have already applied the in-memory transition
    /// before throwing, and ResetForRetry/MarkAsProcessing both throw outside their source status.
    /// </summary>
    private static void ApplyRetryTransition(LlmRequest item, bool retryAsProcessing)
    {
        if (item.Status == RequestStatus.Failed)
        {
            item.ResetForRetry();
        }

        if (retryAsProcessing && item.Status == RequestStatus.Pending)
        {
            item.MarkAsProcessing();
        }
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
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(LlmQueueToProposalWorker)),
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, outcome));

        TaskdeckTelemetry.WorkerItemsProcessed.Add(
            1,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(LlmQueueToProposalWorker)),
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, outcome));
    }

    private readonly record struct WorkerBatchItem(Guid ItemId, bool IsCaptureTriage, DateTimeOffset? ExpectedUpdatedAt = null);
}
