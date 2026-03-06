using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Api.Telemetry;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Api.Workers;

public class ProposalHousekeepingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerSettings _settings;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;
    private readonly ILogger<ProposalHousekeepingWorker> _logger;

    public ProposalHousekeepingWorker(
        IServiceScopeFactory scopeFactory,
        WorkerSettings settings,
        WorkerHeartbeatRegistry workerHeartbeatRegistry,
        ILogger<ProposalHousekeepingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _workerHeartbeatRegistry = workerHeartbeatRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProposalHousekeepingWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            _workerHeartbeatRegistry.ReportHeartbeat(nameof(ProposalHousekeepingWorker));

            try
            {
                await ExpireStaleProposalsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Error in ProposalHousekeepingWorker iteration. {ExceptionSummary}",
                    SensitiveDataRedactor.SummarizeException(ex));
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("ProposalHousekeepingWorker stopped");
    }

    private async Task ExpireStaleProposalsAsync(CancellationToken ct)
    {
        using var activity = TaskdeckTelemetry.ActivitySource.StartActivity(
            "taskdeck.worker.expire_stale_proposals",
            System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag(TaskdeckTelemetryTags.WorkerName, nameof(ProposalHousekeepingWorker));

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pendingProposals = (await unitOfWork.AutomationProposals.GetByStatusAsync(
            ProposalStatus.PendingReview,
            cancellationToken: ct)).ToList();
        var now = DateTime.UtcNow;
        var pendingCount = pendingProposals.Count;
        var expiredCount = 0;
        activity?.SetTag("taskdeck.proposals.pending_count", pendingCount);

        foreach (var proposal in pendingProposals)
        {
            if (ct.IsCancellationRequested) break;

            if (proposal.ExpiresAt <= now)
            {
                try
                {
                    proposal.Expire();
                    expiredCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Failed to expire proposal {ProposalId}. {ExceptionSummary}",
                        proposal.Id,
                        SensitiveDataRedactor.SummarizeException(ex));
                }
            }
        }

        if (expiredCount > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Expired {Count} stale proposals", expiredCount);
        }

        activity?.SetTag("taskdeck.proposals.expired_count", expiredCount);
        TaskdeckTelemetry.HousekeepingExpiredProposals.Add(
            expiredCount,
            new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(ProposalHousekeepingWorker)));
    }
}
