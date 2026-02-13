using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkerSettings _workerSettings;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;

    public HealthController(
        IServiceProvider serviceProvider,
        WorkerSettings workerSettings,
        WorkerHeartbeatRegistry workerHeartbeatRegistry)
    {
        _serviceProvider = serviceProvider;
        _workerSettings = workerSettings;
        _workerHeartbeatRegistry = workerHeartbeatRegistry;
    }

    [HttpGet("live")]
    public IActionResult LiveCheck()
    {
        return Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow });
    }

    [HttpGet("ready")]
    public async Task<IActionResult> ReadyCheck(CancellationToken ct = default)
    {
        var checks = new Dictionary<string, object>();
        var isReady = true;

        // DB connectivity check
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var canConnect = await dbContext.Database.CanConnectAsync(ct);
            if (canConnect)
            {
                checks["database"] = new { status = "Healthy" };
            }
            else
            {
                checks["database"] = new { status = "Unhealthy", error = "Database connectivity check failed" };
                isReady = false;
            }
        }
        catch (Exception ex)
        {
            checks["database"] = new { status = "Unhealthy", error = ex.Message };
            isReady = false;
        }

        // Queue lag check
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var pending = await unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Pending, ct);
            var queueDepth = pending.Count();
            var queueThreshold = Math.Max(_workerSettings.MaxBatchSize * 20, 100);
            var queueHealthy = queueDepth <= queueThreshold;

            checks["queue"] = new
            {
                status = queueHealthy ? "Healthy" : "Degraded",
                depth = queueDepth,
                threshold = queueThreshold
            };

            if (!queueHealthy)
            {
                isReady = false;
            }
        }
        catch (Exception ex)
        {
            checks["queue"] = new { status = "Unhealthy", error = ex.Message };
            isReady = false;
        }

        // Worker heartbeat checks
        var workerChecks = new Dictionary<string, object>();

        if (_workerSettings.EnableAutoQueueProcessing)
        {
            var queueWorkerLastHeartbeat = _workerHeartbeatRegistry.GetLastHeartbeat(nameof(LlmQueueToProposalWorker));
            var maxQueueWorkerStaleness = TimeSpan.FromSeconds(Math.Max(_workerSettings.QueuePollIntervalSeconds * 3, 30));
            var queueWorkerHealthy = queueWorkerLastHeartbeat.HasValue &&
                                     DateTimeOffset.UtcNow - queueWorkerLastHeartbeat.Value <= maxQueueWorkerStaleness;

            workerChecks["queueToProposal"] = new
            {
                status = queueWorkerHealthy ? "Healthy" : "Stale",
                lastHeartbeat = queueWorkerLastHeartbeat
            };

            if (!queueWorkerHealthy)
            {
                isReady = false;
            }
        }
        else
        {
            workerChecks["queueToProposal"] = new
            {
                status = "Disabled",
                lastHeartbeat = (DateTimeOffset?)null
            };
        }

        var housekeepingLastHeartbeat = _workerHeartbeatRegistry.GetLastHeartbeat(nameof(ProposalHousekeepingWorker));
        var housekeepingHealthy = housekeepingLastHeartbeat.HasValue &&
                                  DateTimeOffset.UtcNow - housekeepingLastHeartbeat.Value <= TimeSpan.FromMinutes(3);
        workerChecks["proposalHousekeeping"] = new
        {
            status = housekeepingHealthy ? "Healthy" : "Stale",
            lastHeartbeat = housekeepingLastHeartbeat
        };
        if (!housekeepingHealthy)
        {
            isReady = false;
        }

        checks["workers"] = workerChecks;

        var statusCode = isReady ? 200 : 503;
        return StatusCode(statusCode, new
        {
            status = isReady ? "Ready" : "NotReady",
            timestamp = DateTimeOffset.UtcNow,
            checks
        });
    }
}
