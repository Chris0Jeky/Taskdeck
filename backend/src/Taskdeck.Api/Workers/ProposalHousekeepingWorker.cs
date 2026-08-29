using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Api.Telemetry;

namespace Taskdeck.Api.Workers;

public class ProposalHousekeepingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerSettings _settings;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;
    private readonly ILogger<ProposalHousekeepingWorker> _logger;

    /// <summary>
    /// The archived-board skip count reported by the previous sweep, so the operator log line is
    /// emitted on transitions rather than once a minute forever. Touched only from the single
    /// sequential <see cref="ExecuteAsync"/> loop, so it needs no synchronization.
    /// </summary>
    private int _lastSkippedArchivedBoardCount;

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

        // Use the purpose-built unbounded expiry query (Status == PendingReview AND ExpiresAt < now) rather
        // than a bounded display-order page (#1259): the prior GetByStatusAsync(limit 100) + in-memory
        // ExpiresAt filter could leave genuinely-expired proposals un-expired once the pending backlog
        // exceeded the page (the bottom of the window — typically the oldest/stalest — was dropped).
        //
        // The sweep arrives already partitioned by ADR-0063's archived-board rule (#2197): proposals
        // on an extant archived board are withheld by the query, so this loop cannot expire one, and
        // no save, notification, or audit row is produced for them. They are not dropped — restoring
        // the board makes them eligible on a later cycle.
        var sweep = await unitOfWork.AutomationProposals.GetExpiredAsync(ct);
        var expiredProposals = sweep.Expirable;
        var expiredCount = 0;
        activity?.SetTag("taskdeck.proposals.expired_candidate_count", expiredProposals.Count);
        activity?.SetTag(
            "taskdeck.proposals.skipped_archived_board_count",
            sweep.SkippedArchivedBoardCount);

        // Report what the guard withheld, but only when the figure CHANGES. This sweep runs every
        // 60s and a withheld proposal stays PendingReview until its board is restored, so logging
        // unconditionally emitted ~1,440 identical Information lines a day for a single archived
        // board — which trains operators to filter the line out, costing the signal it exists for.
        // Steady state is therefore Debug; a transition is Information, including the transition
        // back to zero so the condition CLEARING is visible and not just its onset.
        if (sweep.SkippedArchivedBoardCount != _lastSkippedArchivedBoardCount)
        {
            if (sweep.SkippedArchivedBoardCount > 0)
            {
                // Count only — no proposal id, summary, or board name — so this stays non-secret.
                _logger.LogInformation(
                    "Skipped expiring {SkippedCount} stale proposals because their board is archived; "
                        + "restore the board to let them expire.",
                    sweep.SkippedArchivedBoardCount);
            }
            else
            {
                _logger.LogInformation(
                    "No stale proposals are being withheld for archived boards any more.");
            }
        }
        else if (sweep.SkippedArchivedBoardCount > 0)
        {
            _logger.LogDebug(
                "Still skipping {SkippedCount} stale proposals because their board is archived.",
                sweep.SkippedArchivedBoardCount);
        }

        _lastSkippedArchivedBoardCount = sweep.SkippedArchivedBoardCount;

        foreach (var proposal in expiredProposals)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // GetExpiredAsync already filtered to expired PendingReview rows, and this materialized entity
                // keeps that status, so Expire() normally succeeds; this catch is defensive against any Expire()
                // precondition failure. A genuine concurrent approve/reject does NOT mutate this in-memory
                // entity -- it surfaces as a DbUpdateConcurrencyException at SaveChangesAsync below (UpdatedAt is
                // the concurrency token), handled by the outer ExecuteAsync loop, so a decision is never silently
                // overwritten and the rest expire on the next cycle.
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
