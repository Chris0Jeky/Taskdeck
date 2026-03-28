using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Hubs;
using Taskdeck.Api.Realtime;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CompositeBoardRealtimeNotifierTests
{
    [Fact]
    public async Task NotifyBoardMutationAsync_ShouldDelegateToBothInnerNotifiers()
    {
        var mutation = CreateMutation();
        var signalRClientProxy = new RecordingClientProxy();
        var hubContext = new FakeHubContext(signalRClientProxy);
        var signalRNotifier = new SignalRBoardRealtimeNotifier(hubContext);
        var outboundService = new RecordingOutboundWebhookService();
        var webhookNotifier = new WebhookBoardMutationNotifier(
            outboundService,
            new InMemoryLogger<WebhookBoardMutationNotifier>());
        var logger = new InMemoryLogger<CompositeBoardRealtimeNotifier>();
        var notifier = new CompositeBoardRealtimeNotifier(signalRNotifier, webhookNotifier, logger);
        using var cancellationSource = new CancellationTokenSource();

        await notifier.NotifyBoardMutationAsync(mutation, cancellationSource.Token);

        hubContext.LastGroupName.Should().Be(BoardHubGroups.ForBoard(mutation.BoardId));
        signalRClientProxy.MethodName.Should().Be("boardMutation");
        signalRClientProxy.Arguments.Should().ContainSingle();
        ReferenceEquals(signalRClientProxy.Arguments.Single(), mutation).Should().BeTrue();
        signalRClientProxy.CancellationToken.Should().Be(cancellationSource.Token);
        outboundService.Calls.Should().ContainSingle();
        ReferenceEquals(outboundService.Calls.Single().Mutation, mutation).Should().BeTrue();
        outboundService.Calls.Single().CancellationToken.Should().Be(cancellationSource.Token);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyBoardMutationAsync_ShouldIsolateSignalRFailures_AndStillCallWebhookNotifier()
    {
        var mutation = CreateMutation();
        var signalRClientProxy = new RecordingClientProxy
        {
            ExceptionToThrow = new InvalidOperationException("signalr failed")
        };
        var hubContext = new FakeHubContext(signalRClientProxy);
        var signalRNotifier = new SignalRBoardRealtimeNotifier(hubContext);
        var outboundService = new RecordingOutboundWebhookService();
        var webhookNotifier = new WebhookBoardMutationNotifier(
            outboundService,
            new InMemoryLogger<WebhookBoardMutationNotifier>());
        var logger = new InMemoryLogger<CompositeBoardRealtimeNotifier>();
        var notifier = new CompositeBoardRealtimeNotifier(signalRNotifier, webhookNotifier, logger);

        var act = () => notifier.NotifyBoardMutationAsync(mutation, CancellationToken.None);

        await act.Should().NotThrowAsync();
        outboundService.Calls.Should().ContainSingle();
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        var entry = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        entry.Message.Should().Contain("Failed board mutation notification on channel signalr");
        entry.Message.Should().Contain(mutation.EntityType);
        entry.Message.Should().Contain(mutation.Operation);
    }

    [Fact]
    public async Task NotifyBoardMutationAsync_ShouldNotThrow_WhenWebhookReturnsFailure()
    {
        var mutation = CreateMutation();
        var signalRClientProxy = new RecordingClientProxy();
        var hubContext = new FakeHubContext(signalRClientProxy);
        var signalRNotifier = new SignalRBoardRealtimeNotifier(hubContext);
        var outboundService = new RecordingOutboundWebhookService
        {
            ResultToReturn = Result.Failure("webhook_failed", "queue failed")
        };
        var webhookLogger = new InMemoryLogger<WebhookBoardMutationNotifier>();
        var webhookNotifier = new WebhookBoardMutationNotifier(outboundService, webhookLogger);
        var compositeLogger = new InMemoryLogger<CompositeBoardRealtimeNotifier>();
        var notifier = new CompositeBoardRealtimeNotifier(signalRNotifier, webhookNotifier, compositeLogger);

        var act = () => notifier.NotifyBoardMutationAsync(mutation, CancellationToken.None);

        await act.Should().NotThrowAsync();
        outboundService.Calls.Should().ContainSingle();
        webhookLogger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
        compositeLogger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyBoardMutationAsync_ShouldNotThrow_WhenWebhookThrows()
    {
        var mutation = CreateMutation();
        var signalRClientProxy = new RecordingClientProxy();
        var hubContext = new FakeHubContext(signalRClientProxy);
        var signalRNotifier = new SignalRBoardRealtimeNotifier(hubContext);
        var outboundService = new RecordingOutboundWebhookService
        {
            ExceptionToThrow = new InvalidOperationException("webhook failed")
        };
        var webhookLogger = new InMemoryLogger<WebhookBoardMutationNotifier>();
        var webhookNotifier = new WebhookBoardMutationNotifier(outboundService, webhookLogger);
        var compositeLogger = new InMemoryLogger<CompositeBoardRealtimeNotifier>();
        var notifier = new CompositeBoardRealtimeNotifier(signalRNotifier, webhookNotifier, compositeLogger);

        var act = () => notifier.NotifyBoardMutationAsync(mutation, CancellationToken.None);

        await act.Should().NotThrowAsync();
        outboundService.Calls.Should().ContainSingle();
        webhookLogger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        compositeLogger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("card", "created")]
    [InlineData("column", "updated")]
    [InlineData("board", "deleted")]
    public async Task NotifyBoardMutationAsync_ShouldForwardMutationValues_Unmodified(string entityType, string operation)
    {
        var mutation = new BoardRealtimeEvent(
            Guid.NewGuid(),
            entityType,
            operation,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        var signalRClientProxy = new RecordingClientProxy();
        var hubContext = new FakeHubContext(signalRClientProxy);
        var signalRNotifier = new SignalRBoardRealtimeNotifier(hubContext);
        var outboundService = new RecordingOutboundWebhookService();
        var webhookNotifier = new WebhookBoardMutationNotifier(
            outboundService,
            new InMemoryLogger<WebhookBoardMutationNotifier>());
        var notifier = new CompositeBoardRealtimeNotifier(
            signalRNotifier,
            webhookNotifier,
            new InMemoryLogger<CompositeBoardRealtimeNotifier>());

        await notifier.NotifyBoardMutationAsync(mutation, CancellationToken.None);

        ReferenceEquals(signalRClientProxy.Arguments.Single(), mutation).Should().BeTrue();
        ReferenceEquals(outboundService.Calls.Single().Mutation, mutation).Should().BeTrue();
    }

    [Fact]
    public async Task NotifyBoardMutationAsync_ShouldNotThrow_WhenBothChannelsFail()
    {
        var mutation = CreateMutation();
        var signalRClientProxy = new RecordingClientProxy
        {
            ExceptionToThrow = new InvalidOperationException("signalr failed")
        };
        var hubContext = new FakeHubContext(signalRClientProxy);
        var signalRNotifier = new SignalRBoardRealtimeNotifier(hubContext);
        var outboundService = new RecordingOutboundWebhookService
        {
            ExceptionToThrow = new InvalidOperationException("webhook failed")
        };
        var webhookLogger = new InMemoryLogger<WebhookBoardMutationNotifier>();
        var webhookNotifier = new WebhookBoardMutationNotifier(outboundService, webhookLogger);
        var compositeLogger = new InMemoryLogger<CompositeBoardRealtimeNotifier>();
        var notifier = new CompositeBoardRealtimeNotifier(signalRNotifier, webhookNotifier, compositeLogger);

        var act = () => notifier.NotifyBoardMutationAsync(mutation, CancellationToken.None);

        await act.Should().NotThrowAsync();
        // SignalR exception caught by composite's NotifySafeAsync
        compositeLogger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
        // Webhook exception caught by WebhookBoardMutationNotifier's own try-catch
        webhookLogger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    private static BoardRealtimeEvent CreateMutation()
    {
        return new BoardRealtimeEvent(
            Guid.NewGuid(),
            "card",
            "updated",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
    }

    private sealed class RecordingOutboundWebhookService : IOutboundWebhookService
    {
        public List<(BoardRealtimeEvent Mutation, CancellationToken CancellationToken)> Calls { get; } = [];
        public Result ResultToReturn { get; set; } = Result.Success();
        public Exception? ExceptionToThrow { get; set; }

        public Task<Result<OutboundWebhookSubscriptionSecretDto>> CreateSubscriptionAsync(
            Guid boardId,
            Guid actorUserId,
            CreateOutboundWebhookSubscriptionDto dto,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<IReadOnlyList<OutboundWebhookSubscriptionDto>>> ListSubscriptionsAsync(
            Guid boardId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<OutboundWebhookSubscriptionSecretDto>> RotateSecretAsync(
            Guid boardId,
            Guid subscriptionId,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> RevokeSubscriptionAsync(
            Guid boardId,
            Guid subscriptionId,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result> EnqueueBoardMutationAsync(
            BoardRealtimeEvent mutation,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((mutation, cancellationToken));

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class FakeHubContext : IHubContext<BoardsHub>
    {
        private readonly RecordingHubClients _clients;

        public FakeHubContext(RecordingClientProxy clientProxy)
        {
            _clients = new RecordingHubClients(clientProxy);
        }

        public string? LastGroupName => _clients.LastGroupName;

        public IHubClients Clients => _clients;

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        private readonly RecordingClientProxy _clientProxy;

        public RecordingHubClients(RecordingClientProxy clientProxy)
        {
            _clientProxy = clientProxy;
        }

        public string? LastGroupName { get; private set; }

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName)
        {
            LastGroupName = groupName;
            return _clientProxy;
        }
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public Exception? ExceptionToThrow { get; set; }
        public string? MethodName { get; private set; }
        public IReadOnlyList<object?> Arguments { get; private set; } = [];
        public CancellationToken CancellationToken { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            MethodName = method;
            Arguments = args;
            CancellationToken = cancellationToken;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.CompletedTask;
        }
    }
}
