using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Api.Telemetry;

namespace Taskdeck.Api.Workers;

/// <summary>
/// Background worker that periodically deletes audit log entries
/// older than the configured retention period.
/// </summary>
public class AuditRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuditRetentionSettings _settings;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;
    private readonly ILogger<AuditRetentionWorker> _logger;

    public AuditRetentionWorker(
        IServiceScopeFactory scopeFactory,
        AuditRetentionSettings settings,
        WorkerHeartbeatRegistry workerHeartbeatRegistry,
        ILogger<AuditRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _workerHeartbeatRegistry = workerHeartbeatRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AuditRetentionWorker starting (retention={RetentionDays}d, batch={BatchSize}, interval={IntervalHours}h)",
            _settings.MaxRetentionDays,
            _settings.CleanupBatchSize,
            _settings.CleanupIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            _workerHeartbeatRegistry.ReportHeartbeat(nameof(AuditRetentionWorker));

            try
            {
                await CleanupOldEntriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "Error in AuditRetentionWorker iteration. {ExceptionSummary}",
                    SensitiveDataRedactor.SummarizeException(ex));
            }

            await Task.Delay(TimeSpan.FromHours(_settings.CleanupIntervalHours), stoppingToken);
        }

        _logger.LogInformation("AuditRetentionWorker stopped");
    }

    internal async Task CleanupOldEntriesAsync(CancellationToken ct)
    {
        using var activity = TaskdeckTelemetry.ActivitySource.StartActivity(
            "taskdeck.worker.audit_retention_cleanup",
            System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag(TaskdeckTelemetryTags.WorkerName, nameof(AuditRetentionWorker));

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.MaxRetentionDays);

        using var scope = _scopeFactory.CreateScope();
        var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var totalDeleted = await auditRepo.DeleteOldEntriesAsync(
            cutoff,
            _settings.CleanupBatchSize,
            ct);

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "AuditRetentionWorker deleted {Count} entries older than {CutoffDate:u}",
                totalDeleted,
                cutoff);
        }
        else
        {
            _logger.LogDebug(
                "AuditRetentionWorker found no entries older than {CutoffDate:u}",
                cutoff);
        }

        activity?.SetTag("taskdeck.audit.deleted_count", totalDeleted);
    }
}
