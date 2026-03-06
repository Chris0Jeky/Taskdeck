using System.Net.Http.Headers;
using System.Text;
using Taskdeck.Api.Telemetry;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Api.Workers;

public sealed class OutboundWebhookDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorkerSettings _workerSettings;
    private readonly OutboundWebhookSecuritySettings _securitySettings;
    private readonly WorkerHeartbeatRegistry _workerHeartbeatRegistry;
    private readonly ILogger<OutboundWebhookDeliveryWorker> _logger;

    public OutboundWebhookDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        WorkerSettings workerSettings,
        OutboundWebhookSecuritySettings securitySettings,
        WorkerHeartbeatRegistry workerHeartbeatRegistry,
        ILogger<OutboundWebhookDeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _workerSettings = workerSettings;
        _securitySettings = securitySettings;
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
                _logger.LogError(
                    "Unhandled exception in OutboundWebhookDeliveryWorker loop. {ExceptionSummary}",
                    SensitiveDataRedactor.SummarizeException(ex));
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _workerSettings.QueuePollIntervalSeconds)), stoppingToken);
        }

        _logger.LogInformation("OutboundWebhookDeliveryWorker stopped");
    }

    private async Task ProcessDueDeliveriesAsync(CancellationToken cancellationToken)
    {
        var candidates = await GetDueDeliveryCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var maxConcurrency = Math.Clamp(_workerSettings.MaxConcurrency, 1, Math.Max(1, _workerSettings.MaxBatchSize));
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task>(candidates.Count);

        foreach (var candidate in candidates)
        {
            await semaphore.WaitAsync(cancellationToken);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessSingleDeliveryAsync(candidate, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<DeliveryClaimCandidate>> GetDueDeliveryCandidatesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = DateTimeOffset.UtcNow;
        await RecoverStuckProcessingDeliveriesAsync(unitOfWork, now, cancellationToken);

        var dueDeliveries = await unitOfWork.OutboundWebhookDeliveries.GetDuePendingAsync(
            now,
            _workerSettings.MaxBatchSize,
            cancellationToken);

        return dueDeliveries
            .Select(delivery => new DeliveryClaimCandidate(delivery.Id, delivery.UpdatedAt))
            .ToList();
    }

    private async Task ProcessSingleDeliveryAsync(DeliveryClaimCandidate candidate, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var client = _httpClientFactory.CreateClient("OutboundWebhookDelivery");
        var outcome = "unknown";
        var startedAt = DateTime.UtcNow;
        var wasClaimed = false;
        OutboundWebhookDelivery? delivery = null;

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                return;
            }

            var claimedAt = DateTimeOffset.UtcNow;
            wasClaimed = await unitOfWork.OutboundWebhookDeliveries.TryClaimPendingAsync(
                candidate.DeliveryId,
                candidate.ExpectedUpdatedAt,
                claimedAt,
                cancellationToken);
            if (!wasClaimed)
            {
                outcome = "already_claimed";
                return;
            }

            delivery = await unitOfWork.OutboundWebhookDeliveries.GetByIdAsync(candidate.DeliveryId, cancellationToken);
            if (delivery == null)
            {
                outcome = "missing";
                return;
            }

            await unitOfWork.OutboundWebhookDeliveries.ReloadWithSubscriptionAsync(delivery, cancellationToken);
            if (delivery.Status != WebhookDeliveryStatus.Processing)
            {
                outcome = "already_claimed";
                return;
            }

            if (!delivery.Subscription.IsActive)
            {
                delivery.MarkDeadLetter("Webhook subscription is inactive before delivery dispatch.");
                outcome = "dead_letter";
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            if (!Uri.TryCreate(delivery.Subscription.EndpointUrl, UriKind.Absolute, out var endpointUri))
            {
                outcome = MarkFailure(delivery, "Webhook endpoint URI is invalid.");
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            if (!IsEndpointSchemeAllowed(endpointUri))
            {
                outcome = MarkFailure(delivery, "Webhook endpoint URL uses an insecure or unsupported scheme.");
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            if (await OutboundWebhookEndpointGuard.IsHostBlockedAsync(
                    endpointUri.Host,
                    _securitySettings.AllowLocalhostEndpoints,
                    cancellationToken))
            {
                outcome = MarkFailure(delivery, "Webhook endpoint host is not allowed.");
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            var timestamp = DateTimeOffset.UtcNow;
            var signature = OutboundWebhookSignature.Compute(
                delivery.Subscription.SigningSecret,
                timestamp,
                delivery.Payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
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

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (wasClaimed && delivery is not null && delivery.Status == WebhookDeliveryStatus.Processing)
            {
                delivery.ReturnToPending(
                    DateTimeOffset.UtcNow,
                    "Webhook delivery interrupted during worker shutdown.");
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                outcome = "cancelled_requeued";
            }

            return;
        }
        catch (Exception ex)
        {
            if (wasClaimed && delivery is not null)
            {
                if (delivery.Status != WebhookDeliveryStatus.Processing)
                {
                    await unitOfWork.OutboundWebhookDeliveries.ReloadWithSubscriptionAsync(delivery, CancellationToken.None);
                }

                if (delivery.Status == WebhookDeliveryStatus.Processing)
                {
                    outcome = MarkFailure(
                        delivery,
                        SensitiveDataRedactor.Redact(
                            $"Webhook delivery threw {ex.GetType().Name}: {ex.Message}"));
                    await unitOfWork.SaveChangesAsync(CancellationToken.None);
                }
                else
                {
                    outcome = "error_before_processing";
                    _logger.LogError(
                        "Webhook delivery threw {ExceptionType} before reaching Processing state. DeliveryId={DeliveryId}, Status={Status}. {ExceptionSummary}",
                        ex.GetType().Name,
                        candidate.DeliveryId,
                        delivery.Status,
                        SensitiveDataRedactor.SummarizeException(ex));
                }
            }
            else
            {
                outcome = "error_before_processing";
                _logger.LogError(
                    "Webhook delivery threw {ExceptionType} before claim. DeliveryId={DeliveryId}. {ExceptionSummary}",
                    ex.GetType().Name,
                    candidate.DeliveryId,
                    SensitiveDataRedactor.SummarizeException(ex));
            }
        }
        finally
        {
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

    private async Task RecoverStuckProcessingDeliveriesAsync(
        IUnitOfWork unitOfWork,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var processingLeaseSeconds = Math.Max(30, _workerSettings.ProcessingLeaseSeconds);
        var staleBefore = now.AddSeconds(-processingLeaseSeconds);
        var stuckDeliveries = await unitOfWork.OutboundWebhookDeliveries.GetStuckProcessingAsync(
            staleBefore,
            _workerSettings.MaxBatchSize,
            cancellationToken);
        if (stuckDeliveries.Count == 0)
        {
            return;
        }

        foreach (var stuckDelivery in stuckDeliveries)
        {
            stuckDelivery.ReturnToPending(
                now,
                "Recovered stale processing webhook delivery for retry.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "Recovered {RecoveredCount} stale webhook deliveries older than {ProcessingLeaseSeconds}s.",
            stuckDeliveries.Count,
            processingLeaseSeconds);
    }

    private string MarkFailure(
        OutboundWebhookDelivery delivery,
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

    private bool IsEndpointSchemeAllowed(Uri endpointUri)
    {
        var isHttps = string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (isHttps)
        {
            return true;
        }

        var isHttp = string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttp)
        {
            return false;
        }

        var isLocalhost = endpointUri.IsLoopback ||
                          string.Equals(endpointUri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        return _securitySettings.AllowLocalhostEndpoints && isLocalhost;
    }

    private readonly record struct DeliveryClaimCandidate(Guid DeliveryId, DateTimeOffset ExpectedUpdatedAt);
}
