using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Api.Workers;

public class ProposalHousekeepingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerSettings _settings;
    private readonly ILogger<ProposalHousekeepingWorker> _logger;

    public ProposalHousekeepingWorker(
        IServiceScopeFactory scopeFactory,
        WorkerSettings settings,
        ILogger<ProposalHousekeepingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProposalHousekeepingWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
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
                _logger.LogError(ex, "Error in ProposalHousekeepingWorker iteration");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("ProposalHousekeepingWorker stopped");
    }

    private async Task ExpireStaleProposalsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pendingProposals = await unitOfWork.AutomationProposals.GetByStatusAsync(ProposalStatus.PendingReview, cancellationToken: ct);
        var now = DateTime.UtcNow;
        var expiredCount = 0;

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
                    _logger.LogWarning(ex, "Failed to expire proposal {ProposalId}", proposal.Id);
                }
            }
        }

        if (expiredCount > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Expired {Count} stale proposals", expiredCount);
        }
    }
}
