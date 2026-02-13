using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Api.Workers;

public class LlmQueueToProposalWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerSettings _settings;
    private readonly ILogger<LlmQueueToProposalWorker> _logger;

    public LlmQueueToProposalWorker(
        IServiceScopeFactory scopeFactory,
        WorkerSettings settings,
        ILogger<LlmQueueToProposalWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LlmQueueToProposalWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
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
        var planner = scope.ServiceProvider.GetRequiredService<IAutomationPlannerService>();

        var pendingItems = await unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Pending, ct);
        var batch = pendingItems.Take(_settings.MaxBatchSize).ToList();

        foreach (var item in batch)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (item.RetryCount >= _settings.MaxRetries)
                {
                    item.MarkAsFailed($"Max retries ({_settings.MaxRetries}) exceeded");
                    await unitOfWork.SaveChangesAsync(ct);
                    _logger.LogWarning("Queue item {ItemId} exceeded max retries, marked as failed", item.Id);
                    continue;
                }

                item.MarkAsProcessing();
                await unitOfWork.SaveChangesAsync(ct);

                var proposalResult = await planner.ParseInstructionAsync(
                    item.Payload,
                    item.UserId,
                    item.BoardId,
                    ct);

                if (proposalResult.IsSuccess)
                {
                    item.MarkAsCompleted();
                    _logger.LogInformation("Queue item {ItemId} processed successfully, proposal created", item.Id);
                }
                else
                {
                    item.MarkAsFailed(proposalResult.ErrorMessage ?? "Unknown error");
                    _logger.LogWarning("Queue item {ItemId} processing failed: {Error}", item.Id, proposalResult.ErrorMessage);
                }

                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue item {ItemId}", item.Id);
                try
                {
                    item.MarkAsFailed(ex.Message);
                    await unitOfWork.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Failed to save error state for queue item {ItemId}", item.Id);
                }
            }
        }
    }
}
