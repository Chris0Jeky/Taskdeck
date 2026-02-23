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
                    _logger.LogError(ex, "Error in LlmQueueToProposalWorker iteration");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.QueuePollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("LlmQueueToProposalWorker stopped");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var pendingItems = (await unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Pending, ct))
            .Where(item => !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            .OrderBy(item => item.CreatedAt)
            .ToList();
        var triagingCaptureItems = (await unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Processing, ct))
            .Where(item => CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            .OrderBy(item => item.CreatedAt)
            .ToList();

        TaskdeckTelemetry.AutomationQueueBacklog.Record(
            pendingItems.Count,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.QueueName, QueueNameLlm));
        TaskdeckTelemetry.AutomationQueueBacklog.Record(
            triagingCaptureItems.Count,
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
                        await ProcessSingleItemAsync(batchItem.ItemId, ct);
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

    private async Task ProcessSingleItemAsync(Guid itemId, CancellationToken ct)
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

        var item = await unitOfWork.LlmQueue.GetByIdAsync(itemId, ct);
        if (item == null || item.Status != RequestStatus.Pending)
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
            item.MarkAsProcessing();
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            _logger.LogDebug(ex, "Queue item {ItemId} was already claimed by another worker", itemId);
            outcome = "already_claimed";
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing queue item {ItemId}", item.Id);
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
            var parsedPayloadResult = CaptureRequestContract.ParsePayload(item.Payload);
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
                    promptVersion: triageResult.Value.PromptVersion);

                item.UpdatePayload(CaptureRequestContract.SerializePayload(linkedPayload));
                item.MarkAsCompleted();
                await unitOfWork.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Capture item {ItemId} triaged into proposal {ProposalId}",
                    item.Id,
                    triageResult.Value.ProposalId);

                outcome = "completed";
                stopWatch.Stop();
                RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
                return;
            }

            var retryScheduled = await HandleFailureWithRetryAsync(
                unitOfWork,
                item,
                triageResult.ErrorCode,
                triageResult.ErrorMessage ?? "Capture triage failed",
                ct,
                retryAsProcessing: true);

            outcome = retryScheduled ? "failed_retry" : "failed_permanent";
            stopWatch.Stop();
            RecordWorkerProcessingMetrics(stopWatch.Elapsed.TotalMilliseconds, outcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception triaging capture queue item {ItemId}", item.Id);
            var scheduledForRetry = await HandleFailureWithRetryAsync(
                unitOfWork,
                item,
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
                    batch.Add(new WorkerBatchItem(pendingItems[pendingIndex++].Id, IsCaptureTriage: false));
                }
            }
            else
            {
                if (pendingIndex < pendingItems.Count)
                {
                    batch.Add(new WorkerBatchItem(pendingItems[pendingIndex++].Id, IsCaptureTriage: false));
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
        var currentRetryCount = item.RetryCount;
        var shouldRetry = IsTransientFailure(errorCode) && currentRetryCount + 1 < _settings.MaxRetries;

        item.MarkAsFailed(errorMessage);
        await unitOfWork.SaveChangesAsync(ct);

        if (!shouldRetry)
        {
            _logger.LogWarning(
                "Queue item {ItemId} failed permanently after {RetryCount} retries with error code {ErrorCode}: {ErrorMessage}",
                item.Id,
                item.RetryCount,
                errorCode,
                errorMessage);
            return false;
        }

        var backoff = TimeSpan.FromSeconds(GetRetryBackoffSeconds(item.RetryCount));
        if (backoff > TimeSpan.Zero)
        {
            await Task.Delay(backoff, ct);
        }

        item.ResetForRetry();
        if (retryAsProcessing)
        {
            item.MarkAsProcessing();
        }
        await unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Queue item {ItemId} scheduled for retry attempt {RetryCount}",
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
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(LlmQueueToProposalWorker)),
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, outcome));

        TaskdeckTelemetry.WorkerItemsProcessed.Add(
            1,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(LlmQueueToProposalWorker)),
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, outcome));
    }

    private readonly record struct WorkerBatchItem(Guid ItemId, bool IsCaptureTriage, DateTimeOffset? ExpectedUpdatedAt = null);
}
