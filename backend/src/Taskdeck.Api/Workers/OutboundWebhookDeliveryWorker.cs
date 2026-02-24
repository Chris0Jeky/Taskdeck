using System.Net.Http.Headers;
using System.Text;
using Taskdeck.Api.Telemetry;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Workers;

public sealed class OutboundWebhookDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorkerSettings _workerSettings;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;
    private readonly ILogger<OutboundWebhookDeliveryWorker> _logger;

    public OutboundWebhookDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        WorkerSettings workerSettings,
        WorkerHeartbeatRegistry workerHeartbeatRegistry,
        ILogger<OutboundWebhookDeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _workerSettings = workerSettings;
        _workerHeartbeatRegistry = workerHeartbeatRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboundWebhookDeliveryWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            _workerHeartbeatRegistry.ReportHeartbeat(nameof(OutboundWebhookDeliveryWorker));

            try
            {
                await ProcessDueDeliveriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in OutboundWebhookDeliveryWorker loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _workerSettings.QueuePollIntervalSeconds)), stoppingToken);
        }

        _logger.LogInformation("OutboundWebhookDeliveryWorker stopped");
    }

    private async Task ProcessDueDeliveriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dueDeliveries = await unitOfWork.OutboundWebhookDeliveries.GetDuePendingAsync(
            DateTimeOffset.UtcNow,
            _workerSettings.MaxBatchSize,
            cancellationToken);
        if (dueDeliveries.Count == 0)
        {
            return;
        }

        var client = _httpClientFactory.CreateClient("OutboundWebhookDelivery");

        foreach (var delivery in dueDeliveries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var outcome = "unknown";
            var startedAt = DateTime.UtcNow;

            try
            {
                delivery.MarkProcessing();
                await unitOfWork.SaveChangesAsync(cancellationToken);

                var timestamp = DateTimeOffset.UtcNow;
                var signature = OutboundWebhookSignature.Compute(
                    delivery.Subscription.SigningSecret,
                    timestamp,
                    delivery.Payload);

                using var request = new HttpRequestMessage(HttpMethod.Post, delivery.Subscription.EndpointUrl)
                {
                    Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("X-Taskdeck-Webhook-Delivery-Id", delivery.Id.ToString("D"));
                request.Headers.Add("X-Taskdeck-Webhook-Subscription-Id", delivery.SubscriptionId.ToString("D"));
                request.Headers.Add("X-Taskdeck-Webhook-Event", delivery.EventType);
                request.Headers.Add("X-Taskdeck-Webhook-Timestamp", timestamp.ToUnixTimeSeconds().ToString());
                request.Headers.Add("X-Taskdeck-Webhook-Signature", $"sha256={signature}");

                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    delivery.MarkDelivered((int)response.StatusCode);
                    outcome = "delivered";
                }
                else
                {
                    outcome = MarkFailure(delivery, $"Webhook endpoint returned HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                outcome = MarkFailure(delivery, $"Webhook delivery threw {ex.GetType().Name}: {ex.Message}");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            var durationMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            TaskdeckTelemetry.WorkerItemsProcessed.Add(
                1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(OutboundWebhookDeliveryWorker)),
                new KeyValuePair<string, object?>("outcome", outcome));
            TaskdeckTelemetry.WorkerItemProcessingDurationMs.Record(
                durationMs,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.WorkerName, nameof(OutboundWebhookDeliveryWorker)),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    private string MarkFailure(
        Domain.Entities.OutboundWebhookDelivery delivery,
        string errorMessage,
        int? responseStatusCode = null)
    {
        var nextAttempt = delivery.AttemptCount + 1;
        if (nextAttempt >= _workerSettings.MaxRetries)
        {
            delivery.MarkDeadLetter(errorMessage, responseStatusCode);
            return "dead_letter";
        }

        var nextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(GetRetryBackoffSeconds(nextAttempt));
        delivery.ScheduleRetry(errorMessage, nextAttemptAt, responseStatusCode);
        return "retry_scheduled";
    }

    private int GetRetryBackoffSeconds(int attemptNumber)
    {
        if (_workerSettings.RetryBackoffSeconds.Length == 0)
        {
            return 10;
        }

        var index = Math.Clamp(attemptNumber - 1, 0, _workerSettings.RetryBackoffSeconds.Length - 1);
        return Math.Max(0, _workerSettings.RetryBackoffSeconds[index]);
    }
}
