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

public class OutboundWebhookDeliveryWorkerTests
{
    [Fact]
    public async Task ProcessDueDeliveriesAsync_ShouldNotSend_WhenClaimFails()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/webhook",
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
            "https://example.com/webhook",
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
            new WorkerHeartbeatRegistry(),
            NullLogger<OutboundWebhookDeliveryWorker>.Instance);

        await InvokeProcessDueDeliveriesAsync(worker, cancellationSource.Token);

        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.LastErrorMessage.Should().Contain("interrupted during worker shutdown");
        unitOfWork.SaveChangesTokens.Should().Contain(token => !token.CanBeCanceled);
        handler.RequestCount.Should().Be(1);
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

        public FakeOutboundWebhookDeliveryRepository(
            IReadOnlyList<OutboundWebhookDelivery> dueDeliveries,
            IReadOnlyList<OutboundWebhookDelivery> stuckDeliveries,
            bool tryClaimResult)
        {
            _dueDeliveries = dueDeliveries;
            _stuckDeliveries = stuckDeliveries;
            _tryClaimResult = tryClaimResult;
        }

        public Task<bool> TryClaimPendingAsync(
            Guid deliveryId,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset claimedAt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_tryClaimResult);
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
        public IOutboundWebhookSubscriptionRepository OutboundWebhookSubscriptions => null!;
        public IOutboundWebhookDeliveryRepository OutboundWebhookDeliveries { get; }

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
