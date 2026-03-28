using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Tests.Support;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class OutboundWebhookDeliveryWorkerTests
{
    // Use a literal public TEST-NET address so host-policy tests do not depend on DNS behavior.
    private const string AllowedWebhookEndpoint = "https://203.0.113.10/webhook";
    private const string InsecureAllowedWebhookEndpoint = "http://203.0.113.10/webhook";

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldNotSend_WhenClaimFails()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AllowedWebhookEndpoint,
            "secret",
            ["card.*"]);
        var delivery = CreateDeliveryWithSubscription(subscription);
        var deliveryRepository = new FakeOutboundWebhookDeliveryRepository(
            dueDeliveries: [delivery],
            stuckDeliveries: [],
            tryClaimResult: false);
        var unitOfWork = new FakeUnitOfWork(deliveryRepository);

        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var handler = new CountingHandler();
        var httpClientFactory = new SingleClientFactory(new HttpClient(handler));
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings { MaxBatchSize = 5, QueuePollIntervalSeconds = 1 },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(0);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldRequeueDelivery_WhenCancellationOccursDuringSend()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AllowedWebhookEndpoint,
            "secret",
            ["card.*"]);
        var delivery = CreateDeliveryWithSubscription(subscription);
        var deliveryRepository = new FakeOutboundWebhookDeliveryRepository(
            dueDeliveries: [delivery],
            stuckDeliveries: [],
            tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(deliveryRepository);

        using var serviceProvider = BuildServiceProvider(unitOfWork);
        using var cancellationSource = new CancellationTokenSource();
        var handler = new CountingHandler
        {
            OnSend = (_, cancellationToken) =>
            {
                cancellationSource.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
        };
        var httpClientFactory = new SingleClientFactory(new HttpClient(handler));
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings { MaxBatchSize = 5, QueuePollIntervalSeconds = 1 },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, cancellationSource.Token);

        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.LastErrorMessage.Should().Contain("interrupted during worker shutdown");
        unitOfWork.SaveChangesTokens.Should().Contain(token => !token.CanBeCanceled);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldDeadLetter_WhenSubscriptionIsInactiveAfterClaim()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AllowedWebhookEndpoint,
            "secret",
            ["card.*"]);
        subscription.Revoke(Guid.NewGuid());
        var delivery = CreateDeliveryWithSubscription(subscription);
        var deliveryRepository = new FakeOutboundWebhookDeliveryRepository(
            dueDeliveries: [delivery],
            stuckDeliveries: [],
            tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(deliveryRepository);

        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var handler = new CountingHandler();
        var httpClientFactory = new SingleClientFactory(new HttpClient(handler));
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings { MaxBatchSize = 5, QueuePollIntervalSeconds = 1 },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(0);
        delivery.Status.Should().Be(WebhookDeliveryStatus.DeadLetter);
        delivery.LastErrorMessage.Should().Contain("inactive");
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldRejectInsecureEndpointScheme_ForClaimedDelivery()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            InsecureAllowedWebhookEndpoint,
            "secret",
            ["card.*"]);
        var delivery = CreateDeliveryWithSubscription(subscription);
        var deliveryRepository = new FakeOutboundWebhookDeliveryRepository(
            dueDeliveries: [delivery],
            stuckDeliveries: [],
            tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(deliveryRepository);

        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var handler = new CountingHandler();
        var httpClientFactory = new SingleClientFactory(new HttpClient(handler));
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings { MaxBatchSize = 5, QueuePollIntervalSeconds = 1 },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(0);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.LastErrorMessage.Should().Contain("insecure");
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldNotThrow_WhenClaimFailsBeforeProcessingState()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AllowedWebhookEndpoint,
            "secret",
            ["card.*"]);
        var delivery = CreateDeliveryWithSubscription(subscription);
        var deliveryRepository = new FakeOutboundWebhookDeliveryRepository(
            dueDeliveries: [delivery],
            stuckDeliveries: [],
            tryClaimResult: true,
            tryClaimException: new InvalidOperationException(
                "Authorization: Bearer webhook-secret {\"payload\":\"delivery secret\"} token=webhook-token"));
        var unitOfWork = new FakeUnitOfWork(deliveryRepository);

        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var handler = new CountingHandler();
        var httpClientFactory = new SingleClientFactory(new HttpClient(handler));
        var logger = new InMemoryLogger<OutboundWebhookDeliveryWorker>();
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings { MaxBatchSize = 5, QueuePollIntervalSeconds = 1 },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            logger);

        var act = async () => await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        await act.Should().NotThrowAsync();
        handler.RequestCount.Should().Be(0);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        var entry = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        entry.Exception.Should().BeNull();
        entry.Message.Should().Contain("Webhook delivery threw InvalidOperationException before claim");
        entry.Message.Should().Contain($"Authorization: Bearer {SensitiveDataRedactor.RedactedValue}");
        entry.Message.Should().NotContain("webhook-secret");
        entry.Message.Should().NotContain("delivery secret");
        entry.Message.Should().NotContain("webhook-token");
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldRedactSensitiveFailureMessage_WhenDispatchThrowsDuringProcessing()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AllowedWebhookEndpoint,
            "secret",
            ["card.*"]);
        var delivery = CreateDeliveryWithSubscription(subscription);
        var deliveryRepository = new FakeOutboundWebhookDeliveryRepository(
            dueDeliveries: [delivery],
            stuckDeliveries: [],
            tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(deliveryRepository);

        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var handler = new CountingHandler
        {
            OnSend = (_, _) => throw new InvalidOperationException(
                "Authorization: Bearer webhook-secret {\"payload\":\"delivery secret\"} token=webhook-token")
        };
        var httpClientFactory = new SingleClientFactory(new HttpClient(handler));
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings
            {
                MaxBatchSize = 5,
                QueuePollIntervalSeconds = 1,
                MaxRetries = 1
            },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.DeadLetter);
        delivery.LastErrorMessage.Should().Contain("Webhook delivery threw InvalidOperationException");
        delivery.LastErrorMessage.Should().Contain($"Authorization: Bearer {SensitiveDataRedactor.RedactedValue}");
        delivery.LastErrorMessage.Should().NotContain("webhook-secret");
        delivery.LastErrorMessage.Should().NotContain("delivery secret");
        delivery.LastErrorMessage.Should().NotContain("webhook-token");
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldScheduleRetry_WhenEndpointReturnsNonSuccessAndRetriesRemain()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AllowedWebhookEndpoint,
            "secret",
            ["card.*"]);
        var delivery = CreateDeliveryWithSubscription(subscription);
        var deliveryRepository = new FakeOutboundWebhookDeliveryRepository(
            dueDeliveries: [delivery],
            stuckDeliveries: [],
            tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(deliveryRepository);

        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var handler = new CountingHandler
        {
            OnSend = (_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway)
        };
        var httpClientFactory = new SingleClientFactory(new HttpClient(handler));
        var beforeDispatch = DateTimeOffset.UtcNow;
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings
            {
                MaxBatchSize = 5,
                QueuePollIntervalSeconds = 1,
                MaxRetries = 3,
                RetryBackoffSeconds = [12]
            },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(1);
        delivery.LastResponseStatusCode.Should().Be((int)HttpStatusCode.BadGateway);
        delivery.LastErrorMessage.Should().Contain("HTTP 502");
        delivery.NextAttemptAt.Should().BeOnOrAfter(beforeDispatch.AddSeconds(11));
    }

    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldDeadLetter_WhenEndpointReturnsNonSuccessAtRetryLimit()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AllowedWebhookEndpoint,
            "secret",
            ["card.*"]);
        var delivery = CreateDeliveryWithSubscription(subscription);
        var deliveryRepository = new FakeOutboundWebhookDeliveryRepository(
            dueDeliveries: [delivery],
            stuckDeliveries: [],
            tryClaimResult: true);
        var unitOfWork = new FakeUnitOfWork(deliveryRepository);

        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var handler = new CountingHandler
        {
            OnSend = (_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };
        var httpClientFactory = new SingleClientFactory(new HttpClient(handler));
        var worker = new OutboundWebhookDeliveryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            new WorkerSettings
            {
                MaxBatchSize = 5,
                QueuePollIntervalSeconds = 1,
                MaxRetries = 1
            },
            new OutboundWebhookSecuritySettings { AllowLocalhostEndpoints = false },
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        delivery.Status.Should().Be(WebhookDeliveryStatus.DeadLetter);
        delivery.AttemptCount.Should().Be(1);
        delivery.LastResponseStatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        delivery.LastErrorMessage.Should().Contain("HTTP 503");
    }

    private static ServiceProvider BuildServiceProvider(IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        return services.BuildServiceProvider();
    }

    private static OutboundWebhookDelivery CreateDeliveryWithSubscription(OutboundWebhookSubscription subscription)
    {
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            subscription.Id,
            subscription.BoardId,
            "card.updated",
            "{\"event\":\"card.updated\"}");

        var subscriptionProperty = typeof(OutboundWebhookDelivery).GetProperty(
            nameof(OutboundWebhookDelivery.Subscription),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        subscriptionProperty.Should().NotBeNull();
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

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? OnSend { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount += 1;
            if (OnSend is not null)
            {
                return Task.FromResult(OnSend(request, cancellationToken));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FakeOutboundWebhookDeliveryRepository : IOutboundWebhookDeliveryRepository
    {
        private readonly IReadOnlyList<OutboundWebhookDelivery> _dueDeliveries;
        private readonly IReadOnlyList<OutboundWebhookDelivery> _stuckDeliveries;
        private readonly bool _tryClaimResult;
        private readonly Exception? _tryClaimException;

        public FakeOutboundWebhookDeliveryRepository(
            IReadOnlyList<OutboundWebhookDelivery> dueDeliveries,
            IReadOnlyList<OutboundWebhookDelivery> stuckDeliveries,
            bool tryClaimResult,
            Exception? tryClaimException = null)
        {
            _dueDeliveries = dueDeliveries;
            _stuckDeliveries = stuckDeliveries;
            _tryClaimResult = tryClaimResult;
            _tryClaimException = tryClaimException;
        }

        public Task<bool> TryClaimPendingAsync(
            Guid deliveryId,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset claimedAt,
            CancellationToken cancellationToken = default)
        {
            if (_tryClaimException is not null)
            {
                throw _tryClaimException;
            }

            return Task.FromResult(_tryClaimResult);
        }

        public Task ReloadWithSubscriptionAsync(
            OutboundWebhookDelivery delivery,
            CancellationToken cancellationToken = default)
        {
            if (delivery.Status == WebhookDeliveryStatus.Pending)
            {
                delivery.MarkProcessing();
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetDuePendingAsync(
            DateTimeOffset now,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>(_dueDeliveries.Take(limit).ToList());
        }

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetBySubscriptionAsync(
            Guid subscriptionId,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var deliveries = _dueDeliveries.Where(delivery => delivery.SubscriptionId == subscriptionId).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>(deliveries);
        }

        public Task<IReadOnlyList<OutboundWebhookDelivery>> GetStuckProcessingAsync(
            DateTimeOffset staleBefore,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OutboundWebhookDelivery>>(_stuckDeliveries.Take(limit).ToList());
        }

        public Task<OutboundWebhookDelivery?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_dueDeliveries.FirstOrDefault(delivery => delivery.Id == id));
        }

        public Task<IEnumerable<OutboundWebhookDelivery>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<OutboundWebhookDelivery>>(_dueDeliveries.ToList());
        }

        public Task<OutboundWebhookDelivery> AddAsync(OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task UpdateAsync(OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(OutboundWebhookDelivery entity, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public List<CancellationToken> SaveChangesTokens { get; } = [];

        public FakeUnitOfWork(IOutboundWebhookDeliveryRepository outboundWebhookDeliveryRepository)
        {
            OutboundWebhookDeliveries = outboundWebhookDeliveryRepository;
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
        public IKnowledgeDocumentRepository KnowledgeDocuments => null!;
        public IKnowledgeChunkRepository KnowledgeChunks => null!;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesTokens.Add(cancellationToken);
            return Task.FromResult(0);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
