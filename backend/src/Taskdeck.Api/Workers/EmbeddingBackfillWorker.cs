using Taskdeck.Application.Services;
using Taskdeck.Api.Telemetry;

namespace Taskdeck.Api.Workers;

/// <summary>
/// Background worker that drives the embedding backfill service on a
/// configurable interval. Resumable across restarts (the backfill service
/// tracks which entities have embeddings). Uses exponential backoff on
/// consecutive failures to avoid log spam.
/// </summary>
public class EmbeddingBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmbeddingBackfillSettings _settings;
    private readonly WorkerHeartbeatRegistry _heartbeatRegistry;
    private readonly ILogger<EmbeddingBackfillWorker> _logger;

    public EmbeddingBackfillWorker(
        IServiceScopeFactory scopeFactory,
        EmbeddingBackfillSettings settings,
        WorkerHeartbeatRegistry heartbeatRegistry,
        ILogger<EmbeddingBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _heartbeatRegistry = heartbeatRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("EmbeddingBackfillWorker is disabled via configuration");
            return;
        }

        _logger.LogInformation(
            "EmbeddingBackfillWorker starting (batch={BatchSize}, interval={IntervalSeconds}s)",
            _settings.BatchSize,
            _settings.PollIntervalSeconds);

        int consecutiveErrors = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            _heartbeatRegistry.ReportHeartbeat(nameof(EmbeddingBackfillWorker));

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var backfillService = scope.ServiceProvider
                    .GetRequiredService<IEmbeddingBackfillService>();

                var result = await backfillService.ProcessBatchAsync(
                    _settings.BatchSize,
                    stoppingToken);

                consecutiveErrors = 0;

                // If nothing to process, use normal interval
                // If items remain, poll sooner to catch up
                var delay = result.Remaining > 0
                    ? TimeSpan.FromSeconds(1)
                    : TimeSpan.FromSeconds(_settings.PollIntervalSeconds);

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;

                _logger.LogError(
                    "EmbeddingBackfillWorker iteration failed ({ConsecutiveErrors} consecutive). {Error}",
                    consecutiveErrors,
                    SensitiveDataRedactor.SummarizeException(ex));

                var backoffSeconds = CalculateBackoffSeconds(consecutiveErrors);

                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken);
            }
        }

        _logger.LogInformation("EmbeddingBackfillWorker stopped");
    }

    internal int CalculateBackoffSeconds(int consecutiveErrors)
    {
        var cappedErrors = Math.Min(
            Math.Max(consecutiveErrors, 0),
            _settings.MaxConsecutiveErrors);

        var delaySeconds = _settings.PollIntervalSeconds;
        for (var i = 0; i < cappedErrors; i++)
        {
            if (delaySeconds >= _settings.MaxBackoffSeconds / 2)
            {
                return _settings.MaxBackoffSeconds;
            }

            delaySeconds *= 2;
        }

        return Math.Min(_settings.MaxBackoffSeconds, delaySeconds);
    }
}
