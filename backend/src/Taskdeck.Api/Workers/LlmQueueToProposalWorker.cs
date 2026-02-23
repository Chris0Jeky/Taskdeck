using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Api.Telemetry;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Workers;

public class LlmQueueToProposalWorker : BackgroundService
{
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
        var pendingItems = (await unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Pending, ct)).ToList();
        TaskdeckTelemetry.AutomationQueueBacklog.Record(
            pendingItems.Count,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.QueueName, "llm"));

        var batchItemIds = pendingItems
            .Take(_settings.MaxBatchSize)
            .Select(item => item.Id)
            .ToList();

        if (batchItemIds.Count == 0)
        {
            return;
        }

        var maxConcurrency = Math.Clamp(_settings.MaxConcurrency, 1, _settings.MaxBatchSize);
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task>(batchItemIds.Count);

        foreach (var itemId in batchItemIds)
        {
            await semaphore.WaitAsync(ct);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessSingleItemAsync(itemId, ct);
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

    private async Task<bool> HandleFailureWithRetryAsync(
        IUnitOfWork unitOfWork,
        Taskdeck.Domain.Entities.LlmRequest item,
        string? errorCode,
        string errorMessage,
        CancellationToken ct)
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
}
