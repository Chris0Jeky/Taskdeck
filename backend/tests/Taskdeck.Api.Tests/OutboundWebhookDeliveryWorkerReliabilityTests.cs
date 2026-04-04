using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Extends delivery worker tests with reliability and SSRF-at-worker-boundary scenarios:
/// successful delivery, HTTP 5xx retry, HTTP 429 retry, network timeout retry,
/// max-retries dead-letter, SSRF host blocking, signature header presence,
/// and concurrent delivery independence.
/// </summary>
public class OutboundWebhookDeliveryWorkerReliabilityTests
{
    // TEST-NET-3 address — externally routable and safe to use in unit tests
    private const string PublicEndpoint = "https://203.0.113.42/hooks/taskdeck";

    // -----------------------------------------------------------------------
    // Successful delivery
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldMarkDelivered_OnHttp200()
    {
        var (worker, delivery, handler, _, serviceProvider) = BuildWorkerWithResponse(
            HttpStatusCode.OK,
            maxRetries: 3);
        await using var _ = serviceProvider;

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Delivered);
        delivery.AttemptCount.Should().Be(1);
        delivery.DeliveredAt.Should().NotBeNull();
        delivery.LastResponseStatusCode.Should().Be(200);
        delivery.LastErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task ProcessDueDeliveriesAsync_ShouldMarkDelivered_OnAny2xxResponse(HttpStatusCode statusCode)
    {
        var (worker, delivery, handler, _, serviceProvider) = BuildWorkerWithResponse(statusCode, maxRetries: 3);
        await using var _sp = serviceProvider;

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Delivered);
        delivery.LastResponseStatusCode.Should().Be((int)statusCode);
    }

    // -----------------------------------------------------------------------
    // Retry on transient HTTP failures (5xx)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    [InlineData(HttpStatusCode.BadGateway, 502)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 503)]
    [InlineData(HttpStatusCode.GatewayTimeout, 504)]
    public async Task ProcessDueDeliveriesAsync_ShouldScheduleRetry_On5xxWithRetriesRemaining(
        HttpStatusCode statusCode, int expectedStatusCode)
    {
        var beforeDispatch = DateTimeOffset.UtcNow;
        var (worker, delivery, handler, _, serviceProvider) = BuildWorkerWithResponse(
            statusCode,
            maxRetries: 5,
            retryBackoffSeconds: [10, 30, 60]);
        await using var _sp = serviceProvider;

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending,
            $"HTTP {expectedStatusCode} should trigger a retry when retries remain");
        delivery.AttemptCount.Should().Be(1);
        delivery.LastResponseStatusCode.Should().Be(expectedStatusCode);
        delivery.NextAttemptAt.Should().BeOnOrAfter(beforeDispatch.AddSeconds(9));
        delivery.LastErrorMessage.Should().Contain($"HTTP {expectedStatusCode}");
    }

    // -----------------------------------------------------------------------
    // Retry on HTTP 429 (rate-limited)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldScheduleRetry_OnHttp429()
    {
        var beforeDispatch = DateTimeOffset.UtcNow;
        var (worker, delivery, handler, _, serviceProvider) = BuildWorkerWithResponse(
            HttpStatusCode.TooManyRequests,
            maxRetries: 3,
            retryBackoffSeconds: [60]);
        await using var _sp = serviceProvider;

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending,
            "HTTP 429 is a transient error and should be retried");
        delivery.AttemptCount.Should().Be(1);
        delivery.LastResponseStatusCode.Should().Be(429);
        delivery.NextAttemptAt.Should().BeOnOrAfter(beforeDispatch.AddSeconds(59));
    }

    // -----------------------------------------------------------------------
    // Retry on network timeout
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldScheduleRetry_OnNetworkTimeout()
    {
        var subscription = MakeSubscription(PublicEndpoint);
        var delivery = MakeDelivery(subscription);
        var repository = new FakeDeliveryRepository([delivery], [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);

        var handler = new CapturingHandler
        {
            OnSend = (_, ct) => throw new TaskCanceledException("simulated timeout", new TimeoutException())
        };
        var worker = BuildWorker(serviceProvider, new HttpClient(handler), maxRetries: 3);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending,
            "a network timeout should be treated as a transient failure and retried");
        delivery.AttemptCount.Should().Be(1);
        delivery.LastErrorMessage.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // Max retries exhausted → dead letter
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldDeadLetter_WhenMaxRetriesExhaustedOn5xx()
    {
        var (worker, delivery, handler, _, serviceProvider) = BuildWorkerWithResponse(
            HttpStatusCode.InternalServerError,
            maxRetries: 1);
        await using var _sp = serviceProvider;

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.DeadLetter,
            "after max retries the delivery must move to dead-letter");
        delivery.AttemptCount.Should().Be(1);
        delivery.LastErrorMessage.Should().Contain("HTTP 500");
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldDeadLetter_WhenMaxRetriesExhaustedOnException()
    {
        var subscription = MakeSubscription(PublicEndpoint);
        var delivery = MakeDelivery(subscription);
        var repository = new FakeDeliveryRepository([delivery], [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);

        var handler = new CapturingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("connection refused")
        };
        var worker = BuildWorker(serviceProvider, new HttpClient(handler), maxRetries: 1);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.DeadLetter,
            "a hard exception at max retries must dead-letter the delivery");
    }

    // -----------------------------------------------------------------------
    // SSRF guard at the worker boundary
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.0.0.1/hook")]
    [InlineData("https://192.168.1.100/hook")]
    [InlineData("https://172.16.5.5/hook")]
    [InlineData("https://169.254.169.254/hook")]
    public async Task ProcessDueDeliveriesAsync_ShouldNotSendRequest_WhenEndpointIsPrivateIp(string endpointUrl)
    {
        var subscription = MakeSubscription(endpointUrl);
        var delivery = MakeDelivery(subscription);
        var repository = new FakeDeliveryRepository([delivery], [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);

        var handler = new CapturingHandler();
        // allowLocalhostEndpoints: false — private IPs are blocked
        var worker = BuildWorker(serviceProvider, new HttpClient(handler), maxRetries: 3, allowLocalhost: false);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(0,
            $"the endpoint {endpointUrl} is a private/reserved address and must be blocked by the SSRF guard");
        // Worker should record a failure on the delivery — it schedules retry or dead-letters
        delivery.LastErrorMessage.Should().Contain("not allowed");
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldNotSendRequest_WhenEndpointIsLocalhost()
    {
        var subscription = MakeSubscription("http://localhost/hook");
        var delivery = MakeDelivery(subscription);
        var repository = new FakeDeliveryRepository([delivery], [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);

        var handler = new CapturingHandler();
        // allowLocalhostEndpoints: false
        var worker = BuildWorker(serviceProvider, new HttpClient(handler), maxRetries: 3, allowLocalhost: false);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(0,
            "localhost endpoints must be blocked when AllowLocalhostEndpoints is false");
        delivery.LastErrorMessage.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // Signature header is sent on delivery
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldIncludeSignatureHeader_OnDelivery()
    {
        var subscription = MakeSubscription(PublicEndpoint, signingSecret: "test-signing-secret");
        var delivery = MakeDelivery(subscription, payload: "{\"event\":\"card.created\"}");
        var repository = new FakeDeliveryRepository([delivery], [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);

        HttpRequestMessage? capturedRequest = null;
        var handler = new CapturingHandler
        {
            OnSend = (req, _) =>
            {
                capturedRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        };
        var worker = BuildWorker(serviceProvider, new HttpClient(handler), maxRetries: 3);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.TryGetValues("X-Taskdeck-Webhook-Signature", out var sigValues)
            .Should().BeTrue("the HMAC signature header must be present");
        var sig = sigValues!.Single();
        sig.Should().StartWith("sha256=", "the signature header must use the sha256= prefix");
        sig.Should().MatchRegex(@"^sha256=[a-f0-9]{64}$", "the signature must be a 64-char lowercase hex string");
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldIncludeDeliveryIdAndSubscriptionIdHeaders()
    {
        var subscription = MakeSubscription(PublicEndpoint);
        var delivery = MakeDelivery(subscription);
        var repository = new FakeDeliveryRepository([delivery], [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);

        HttpRequestMessage? capturedRequest = null;
        var handler = new CapturingHandler
        {
            OnSend = (req, _) =>
            {
                capturedRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        };
        var worker = BuildWorker(serviceProvider, new HttpClient(handler), maxRetries: 3);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.TryGetValues("X-Taskdeck-Webhook-Delivery-Id", out var deliveryIdValues)
            .Should().BeTrue();
        deliveryIdValues!.Single().Should().Be(delivery.Id.ToString("D"));

        capturedRequest.Headers.TryGetValues("X-Taskdeck-Webhook-Subscription-Id", out var subIdValues)
            .Should().BeTrue();
        subIdValues!.Single().Should().Be(subscription.Id.ToString("D"));
    }

    // -----------------------------------------------------------------------
    // Concurrent deliveries are independent (no duplicate delivery)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldDeliverEachDeliveryExactlyOnce_WhenMultiplePending()
    {
        var subscription = MakeSubscription(PublicEndpoint);
        var deliveries = Enumerable.Range(0, 5)
            .Select(_ => MakeDelivery(subscription))
            .ToList();

        var repository = new FakeDeliveryRepository(deliveries, [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);

        var handler = new CapturingHandler();
        var worker = BuildWorker(serviceProvider, new HttpClient(handler), maxRetries: 3, maxBatchSize: 10, maxConcurrency: 5);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(5, "each of the 5 deliveries should be sent exactly once");
        deliveries.Should().AllSatisfy(d =>
            d.Status.Should().Be(WebhookDeliveryStatus.Delivered));
    }

    // -----------------------------------------------------------------------
    // Signature HMAC value matches expected computation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessDueDeliveriesAsync_SignatureValue_ShouldMatchExpectedHmac()
    {
        // This test verifies the *value* of the HMAC header, not just its format.
        // It captures the timestamp from the X-Taskdeck-Webhook-Timestamp header that the worker
        // sends alongside the signature, then recomputes the expected HMAC and compares.
        const string signingSecret = "hmac-verification-secret";
        const string payload = "{\"event\":\"card.created\",\"id\":\"abc\"}";

        var subscription = MakeSubscription(PublicEndpoint, signingSecret: signingSecret);
        var delivery = MakeDelivery(subscription, payload: payload);
        var repository = new FakeDeliveryRepository([delivery], [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);

        HttpRequestMessage? capturedRequest = null;
        var handler = new CapturingHandler
        {
            OnSend = (req, _) =>
            {
                capturedRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        };
        var worker = BuildWorker(serviceProvider, new HttpClient(handler), maxRetries: 3);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        capturedRequest.Should().NotBeNull();

        // Extract the timestamp header the worker embedded alongside the signature.
        capturedRequest!.Headers.TryGetValues("X-Taskdeck-Webhook-Timestamp", out var tsValues)
            .Should().BeTrue("timestamp header must be present to allow signature verification");
        var timestampSeconds = long.Parse(tsValues!.Single());
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);

        // Recompute the expected signature using the same algorithm as the production code.
        var expectedSignature = OutboundWebhookSignature.Compute(signingSecret, timestamp, payload);

        capturedRequest.Headers.TryGetValues("X-Taskdeck-Webhook-Signature", out var sigValues)
            .Should().BeTrue("signature header must be present");
        var actualSig = sigValues!.Single();
        actualSig.Should().Be($"sha256={expectedSignature}",
            "the HMAC value must match OutboundWebhookSignature.Compute for the same key, timestamp, and payload");
    }

    // -----------------------------------------------------------------------
    // Builder helpers
    // -----------------------------------------------------------------------

    private static (OutboundWebhookDeliveryWorker worker,
        OutboundWebhookDelivery delivery,
        CapturingHandler handler,
        FakeUnitOfWork unitOfWork,
        ServiceProvider serviceProvider)
        BuildWorkerWithResponse(
            HttpStatusCode statusCode,
            int maxRetries,
            int[]? retryBackoffSeconds = null,
            bool allowLocalhost = false)
    {
        var subscription = MakeSubscription(PublicEndpoint);
        var delivery = MakeDelivery(subscription);
        var repository = new FakeDeliveryRepository([delivery], [], tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(repository);
        // NOTE: do NOT use 'using' here — the caller owns the lifetime and must dispose after the test.
        var serviceProvider = BuildServiceProvider(unitOfWork);

        var handler = new CapturingHandler
        {
            OnSend = (_, _) => new HttpResponseMessage(statusCode)
        };

        var worker = BuildWorker(
            serviceProvider,
            new HttpClient(handler),
            maxRetries,
            retryBackoffSeconds,
            allowLocalhost: allowLocalhost);

        return (worker, delivery, handler, unitOfWork, serviceProvider);
    }

    private static OutboundWebhookDeliveryWorker BuildWorker(
        IServiceProvider serviceProvider,
        HttpClient httpClient,
        int maxRetries = 3,
        int[]? retryBackoffSeconds = null,
        bool allowLocalhost = false,
        int maxBatchSize = 5,
        int maxConcurrency = 1)
    {
        return new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new SingleClientFactory(httpClient),
            new WorkerSettings
            {
                MaxBatchSize = maxBatchSize,
                QueuePollIntervalSeconds = 1,
                MaxRetries = maxRetries,
                MaxConcurrency = maxConcurrency,
                RetryBackoffSeconds = retryBackoffSeconds ?? [10]
            },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = allowLocalhost },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);
    }

    private static ServiceProvider BuildServiceProvider(IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        return services.BuildServiceProvider();
    }

    private static OutboundWebhookSubscription MakeSubscription(
        string endpointUrl = PublicEndpoint,
        string signingSecret = "default-secret")
    {
        return new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            endpointUrl,
            signingSecret,
            ["card.*"]);
    }

    private static OutboundWebhookDelivery MakeDelivery(
        OutboundWebhookSubscription subscription,
        string payload = "{\"event\":\"card.updated\"}")
    {
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            subscription.Id,
            subscription.BoardId,
            "card.updated",
            payload);

        // Wire the navigation property the same way the production EF query does.
        var subscriptionProperty = typeof(OutboundWebhookDelivery).GetProperty(
            nameof(OutboundWebhookDelivery.Subscription),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        subscriptionProperty!.SetValue(delivery, subscription);

        return delivery;
    }

    private static async Task InvokeProcessDueDeliveriesAsync(
        OutboundWebhookDeliveryWorker worker,
        CancellationToken cancellationToken)
    {
        var method = typeof(OutboundWebhookDeliveryWorker).GetMethod(
            "ProcessDueDeliveriesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var invocation = method!.Invoke(worker, [cancellationToken]);
        invocation.Should().NotBeNull();
        await (Task)invocation!;
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? OnSend { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            if (OnSend is not null)
            {
                return Task.FromResult(OnSend(request, cancellationToken));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FakeDeliveryRepository : IOutboundWebhookDeliveryRepository
    {
        private readonly IReadOnlyList<OutboundWebhookDelivery> _due;
        private readonly IReadOnlyList<OutboundWebhookDelivery> _stuck;
        private readonly bool _tryClaimResult;
        private readonly Exception? _tryClaimException;

        public FakeDeliveryRepository(
            IReadOnlyList<OutboundWebhookDelivery> due,
            IReadOnlyList<OutboundWebhookDelivery> stuck,
            bool tryClaimResult,
            Exception? tryClaimException = null)
        {
            _due = due;
            _stuck = stuck;
            _tryClaimResult = tryClaimResult;
            _tryClaimException = tryClaimException;
        }

        public Task<bool> TryClaimPendingAsync(
            Guid deliveryId, DateTimeOffset expectedUpdatedAt,
            DateTimeOffset claimedAt, CancellationToken cancellationToken = default)
        {
            if (_tryClaimException is not null) throw _tryClaimException;
            return Task.FromResult(_tryClaimResult);
        }

        public Task ReloadWithSubscriptionAsync(
            OutboundWebhookDelivery delivery, CancellationToken cancellationToken = default)
        {
            if (delivery.Status == WebhookDeliveryStatus.Pending)
            {
                delivery.MarkProcessing();
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetDuePendingAsync(
            DateTimeOffset now, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>(_due.Take(limit).ToList());

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetBySubscriptionAsync(
            Guid subscriptionId, int limit = 100, CancellationToken cancellationToken = default)
        {
            var result = _due.Where(d => d.SubscriptionId == subscriptionId).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>(result);
        }

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetStuckProcessingAsync(
            DateTimeOffset staleBefore, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>(_stuck.Take(limit).ToList());

        public Task<OutboundWebhookDelivery?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_due.FirstOrDefault(d => d.Id == id));

        public Task<IEnumerable<OutboundWebhookDelivery>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<OutboundWebhookDelivery>>(_due.ToList());

        public Task<OutboundWebhookDelivery> AddAsync(OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public FakeUnitOfWork(IOutboundWebhookDeliveryRepository deliveries)
        {
            OutboundWebhookDeliveries = deliveries;
        }

        public IBoardRepository Boards => null!;
        public IColumnRepository Columns => null!;
        public ICardRepository Cards => null!;
        public ICardCommentRepository CardComments => null!;
        public ILabelRepository Labels => null!;
        public IUserRepository Users => null!;
        public IBoardAccessRepository BoardAccesses => null!;
        public IAuditLogRepository AuditLogs => null!;
        public ILlmQueueRepository LlmQueue => null!;
        public IAutomationProposalRepository AutomationProposals => null!;
        public IArchiveItemRepository ArchiveItems => null!;
        public IChatSessionRepository ChatSessions => null!;
        public IChatMessageRepository ChatMessages => null!;
        public ICommandRunRepository CommandRuns => null!;
        public INotificationRepository Notifications => null!;
        public INotificationPreferenceRepository NotificationPreferences => null!;
        public IUserPreferenceRepository UserPreferences => null!;
        public IOutboundWebhookSubscriptionRepository OutboundWebhookSubscriptions => null!;
        public IOutboundWebhookDeliveryRepository OutboundWebhookDeliveries { get; }
        public ILlmUsageRecordRepository LlmUsageRecords => null!;
        public IAgentProfileRepository AgentProfiles => null!;
        public IAgentRunRepository AgentRuns => null!;
        public IKnowledgeDocumentRepository KnowledgeDocuments => null!;
        public IKnowledgeChunkRepository KnowledgeChunks => null!;
        public IExternalLoginRepository ExternalLogins => null!;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
