using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Health;
using Taskdeck.Api.Telemetry;
using Taskdeck.Api.Workers;
using Taskdeck.Application.DTOs;
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
    private readonly RedisBackplaneHealthCheck _redisHealthCheck;

    public HealthController(
        IServiceProvider serviceProvider,
        WorkerSettings workerSettings,
        WorkerHeartbeatRegistry workerHeartbeatRegistry,
        RedisBackplaneHealthCheck redisHealthCheck)
    {
        _serviceProvider = serviceProvider;
        _workerSettings = workerSettings;
        _workerHeartbeatRegistry = workerHeartbeatRegistry;
        _redisHealthCheck = redisHealthCheck;
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
            var pendingList = pending.ToList();
            var queueDepth = pendingList.Count(item => !CaptureRequestContract.IsCaptureRequestType(item.RequestType));
            var captureDepth = pendingList.Count - queueDepth;
            var queueThreshold = Math.Max(_workerSettings.MaxBatchSize * 20, 100);
            var queueHealthy = queueDepth <= queueThreshold;

            checks["queue"] = new
            {
                status = queueHealthy ? "Healthy" : "Degraded",
                depth = queueDepth,
                totalDepth = pendingList.Count,
                captureDepth,
                threshold = queueThreshold
            };
            TaskdeckTelemetry.AutomationQueueBacklog.Record(
                queueDepth,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.QueueName, "llm"));

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

        // SignalR backplane (Redis) health check
        try
        {
            var redisStatus = await _redisHealthCheck.CheckAsync(ct);
            checks["signalrBackplane"] = redisStatus;

            // Unhealthy Redis degrades overall readiness only when Redis is configured.
            // NotConfigured is normal for local/single-instance deployments.
            if (redisStatus.Status == "Unhealthy")
            {
                isReady = false;
            }
        }
        catch (Exception ex)
        {
            checks["signalrBackplane"] = new RedisHealthStatus("Unhealthy", ex.Message);
            isReady = false;
        }

        // Worker heartbeat checks
        var workerChecks = new Dictionary<string, object>();

        if (_workerSettings.EnableAutoQueueProcessing)
        {
            var queueWorkerLastHeartbeat = _workerHeartbeatRegistry.GetLastHeartbeat(nameof(LlmQueueToProposalWorker));
            var maxQueueWorkerStaleness = TimeSpan.FromSeconds(Math.Max(_workerSettings.QueuePollIntervalSeconds * 3, 30));
            var withinStartupGrace = DateTimeOffset.UtcNow - _workerHeartbeatRegistry.StartupTime <= TimeSpan.FromSeconds(30);
            var queueWorkerStaleness = queueWorkerLastHeartbeat.HasValue
                ? DateTimeOffset.UtcNow - queueWorkerLastHeartbeat.Value
                : (TimeSpan?)null;
            var queueWorkerHealthy = (queueWorkerLastHeartbeat.HasValue &&
                                      queueWorkerStaleness <= maxQueueWorkerStaleness)
                                     || (!queueWorkerLastHeartbeat.HasValue && withinStartupGrace);

            if (queueWorkerStaleness.HasValue)
            {
                TaskdeckTelemetry.WorkerHeartbeatStalenessSeconds.Record(
                    queueWorkerStaleness.Value.TotalSeconds,
                    new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(LlmQueueToProposalWorker)),
                    new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, queueWorkerHealthy ? "healthy" : "stale"));
            }

            workerChecks["queueToProposal"] = new
            {
                status = queueWorkerLastHeartbeat.HasValue
                    ? (queueWorkerHealthy ? "Healthy" : "Stale")
                    : (withinStartupGrace ? "Starting" : "Stale"),
                lastHeartbeat = queueWorkerLastHeartbeat,
                stalenessSeconds = queueWorkerStaleness?.TotalSeconds,
                maxStalenessSeconds = maxQueueWorkerStaleness.TotalSeconds
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
        var housekeepingWithinStartupGrace = DateTimeOffset.UtcNow - _workerHeartbeatRegistry.StartupTime <= TimeSpan.FromSeconds(30);
        var housekeepingStaleness = housekeepingLastHeartbeat.HasValue
            ? DateTimeOffset.UtcNow - housekeepingLastHeartbeat.Value
            : (TimeSpan?)null;
        var housekeepingHealthy = (housekeepingLastHeartbeat.HasValue &&
                                   housekeepingStaleness <= TimeSpan.FromMinutes(3))
                                  || (!housekeepingLastHeartbeat.HasValue && housekeepingWithinStartupGrace);
        if (housekeepingStaleness.HasValue)
        {
            TaskdeckTelemetry.WorkerHeartbeatStalenessSeconds.Record(
                housekeepingStaleness.Value.TotalSeconds,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(ProposalHousekeepingWorker)),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, housekeepingHealthy ? "healthy" : "stale"));
        }

        workerChecks["proposalHousekeeping"] = new
        {
            status = housekeepingLastHeartbeat.HasValue
                ? (housekeepingHealthy ? "Healthy" : "Stale")
                : (housekeepingWithinStartupGrace ? "Starting" : "Stale"),
            lastHeartbeat = housekeepingLastHeartbeat,
            stalenessSeconds = housekeepingStaleness?.TotalSeconds,
            maxStalenessSeconds = TimeSpan.FromMinutes(3).TotalSeconds
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
