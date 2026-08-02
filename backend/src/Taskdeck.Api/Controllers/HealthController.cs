using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
    private readonly LlmProviderSettings _llmProviderSettings;
    private readonly IWebHostEnvironment _environment;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;
    private readonly RedisBackplaneHealthCheck _redisHealthCheck;
    private readonly CircuitBreakerStateTracker _circuitBreakerTracker;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IServiceProvider serviceProvider,
        WorkerSettings workerSettings,
        LlmProviderSettings llmProviderSettings,
        IWebHostEnvironment environment,
        WorkerHeartbeatRegistry workerHeartbeatRegistry,
        RedisBackplaneHealthCheck redisHealthCheck,
        CircuitBreakerStateTracker circuitBreakerTracker,
        ILogger<HealthController> logger)
    {
        _serviceProvider = serviceProvider;
        _workerSettings = workerSettings;
        _llmProviderSettings = llmProviderSettings;
        _environment = environment;
        _workerHeartbeatRegistry = workerHeartbeatRegistry;
        _redisHealthCheck = redisHealthCheck;
        _circuitBreakerTracker = circuitBreakerTracker;
        _logger = logger;
    }

    [HttpGet("live")]
    [AllowAnonymous]
    public IActionResult LiveCheck()
    {
        return Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow });
    }

    [HttpGet("ready")]
    [AllowAnonymous]
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
            _logger.LogError(ex, "Health check: database connectivity failed");
            checks["database"] = new { status = "Unhealthy", error = "Connection failed" };
            isReady = false;
        }

        // Queue lag check
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            // Count primitives instead of materializing every Pending row on this frequently-polled
            // readiness probe (#1251): queueDepth = Pending non-capture (the automation backlog the
            // threshold gates on), captureDepth = Pending capture-triage, totalDepth = their sum.
            var queueDepth = await unitOfWork.LlmQueue.CountPendingNonCaptureAsync(ct);
            var captureDepth = await unitOfWork.LlmQueue.CountPendingCaptureAsync(ct);
            var totalDepth = queueDepth + captureDepth;
            var queueThreshold = Math.Max(_workerSettings.MaxBatchSize * 20, 100);
            var queueHealthy = queueDepth <= queueThreshold;

            checks["queue"] = new
            {
                status = queueHealthy ? "Healthy" : "Degraded",
                depth = queueDepth,
                totalDepth,
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
            _logger.LogError(ex, "Health check: queue status query failed");
            checks["queue"] = new { status = "Unhealthy", error = "Connection failed" };
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
            _logger.LogError(ex, "Health check: SignalR backplane connectivity failed");
            checks["signalrBackplane"] = new RedisHealthStatus("Unhealthy", "Connection failed");
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

            // Transcript map-reduce pulses the shared heartbeat immediately before and after each
            // provider completion, and again between sequential queue items. Readiness therefore
            // needs to tolerate one selected-provider timeout plus normal poll allowance - never
            // the whole MaxChunkCount/MaxBatchSize run, which would mask a first-call wedge.
            var transcriptWorkerLastHeartbeat = _workerHeartbeatRegistry.GetLastHeartbeat(nameof(TranscriptTriageWorker));
            var maxTranscriptWorkerStaleness = CalculateTranscriptWorkerMaxStaleness(
                _workerSettings,
                _llmProviderSettings,
                _environment.EnvironmentName);
            var transcriptWorkerNow = DateTimeOffset.UtcNow;
            var transcriptWorkerStaleness = transcriptWorkerLastHeartbeat.HasValue
                ? transcriptWorkerNow - transcriptWorkerLastHeartbeat.Value
                : (TimeSpan?)null;
            var transcriptWorkerHealthy = IsWorkerHeartbeatHealthy(
                transcriptWorkerLastHeartbeat,
                _workerHeartbeatRegistry.StartupTime,
                maxTranscriptWorkerStaleness,
                transcriptWorkerNow);

            if (transcriptWorkerStaleness.HasValue)
            {
                TaskdeckTelemetry.WorkerHeartbeatStalenessSeconds.Record(
                    transcriptWorkerStaleness.Value.TotalSeconds,
                    new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(TranscriptTriageWorker)),
                    new KeyValuePair<string, object?>(TaskdeckTelemetryTags.Outcome, transcriptWorkerHealthy ? "healthy" : "stale"));
            }

            workerChecks["transcriptTriage"] = new
            {
                status = transcriptWorkerLastHeartbeat.HasValue
                    ? (transcriptWorkerHealthy ? "Healthy" : "Stale")
                    : (withinStartupGrace ? "Starting" : "Stale"),
                lastHeartbeat = transcriptWorkerLastHeartbeat,
                stalenessSeconds = transcriptWorkerStaleness?.TotalSeconds,
                maxStalenessSeconds = maxTranscriptWorkerStaleness.TotalSeconds
            };

            if (!transcriptWorkerHealthy)
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
            workerChecks["transcriptTriage"] = new
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

        // Circuit breaker state for external HTTP clients.
        // Open circuits are reported but do NOT fail the readiness probe because
        // LLM and OAuth providers are optional/degradeable -- the system falls back
        // to mock responses or cached tokens when a provider is unavailable.
        // Operators can monitor the circuitBreakers section for open circuits.
        var circuitBreakerStates = _circuitBreakerTracker.GetAll();
        if (circuitBreakerStates.Count > 0)
        {
            var circuitChecks = new Dictionary<string, object>();
            var anyOpen = false;
            foreach (var (name, snapshot) in circuitBreakerStates)
            {
                circuitChecks[name] = new
                {
                    state = snapshot.State.ToString(),
                    lastTransitionUtc = snapshot.LastTransitionUtc
                };

                if (snapshot.State == CircuitState.Open)
                {
                    anyOpen = true;
                }
            }
            circuitChecks["_summary"] = new { status = anyOpen ? "Degraded" : "Healthy" };
            checks["circuitBreakers"] = circuitChecks;
        }
        else
        {
            checks["circuitBreakers"] = new { status = "AllClosed" };
        }

        var statusCode = isReady ? 200 : 503;
        return StatusCode(statusCode, new
        {
            status = isReady ? "Ready" : "NotReady",
            timestamp = DateTimeOffset.UtcNow,
            checks
        });
    }

    internal static TimeSpan CalculateTranscriptWorkerMaxStaleness(
        WorkerSettings workerSettings,
        LlmProviderSettings llmProviderSettings,
        string? environmentName)
    {
        ArgumentNullException.ThrowIfNull(workerSettings);
        ArgumentNullException.ThrowIfNull(llmProviderSettings);

        var pollAllowanceSeconds = Math.Max((long)workerSettings.QueuePollIntervalSeconds * 3, 30L);
        return TimeSpan.FromSeconds(
            pollAllowanceSeconds + GetSelectedProviderTimeoutSeconds(llmProviderSettings, environmentName));
    }

    internal static bool IsWorkerHeartbeatHealthy(
        DateTimeOffset? lastHeartbeat,
        DateTimeOffset startupTime,
        TimeSpan maxStaleness,
        DateTimeOffset now)
    {
        var withinStartupGrace = now - startupTime <= TimeSpan.FromSeconds(30);
        return (lastHeartbeat.HasValue && now - lastHeartbeat.Value <= maxStaleness)
               || (!lastHeartbeat.HasValue && withinStartupGrace);
    }

    private static int GetSelectedProviderTimeoutSeconds(
        LlmProviderSettings settings,
        string? environmentName)
    {
        // Readiness must reflect the provider selected at runtime, not merely the requested name:
        // disabled live providers, development policy, or invalid settings all resolve to Mock.
        return LlmProviderSelectionPolicy.Evaluate(settings, environmentName).ProviderKind switch
        {
            LlmProviderKind.OpenAi => settings.OpenAi?.TimeoutSeconds is > 0 ? settings.OpenAi.TimeoutSeconds : 30,
            LlmProviderKind.Gemini => settings.Gemini?.TimeoutSeconds is > 0 ? settings.Gemini.TimeoutSeconds : 30,
            LlmProviderKind.Ollama => settings.Ollama?.TimeoutSeconds is > 0 ? settings.Ollama.TimeoutSeconds : 120,
            _ => 30 // Mock and policy-disabled modes perform no live completion.
        };
    }
}
