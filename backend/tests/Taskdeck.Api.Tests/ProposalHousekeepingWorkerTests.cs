using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Tests.Support;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ProposalHousekeepingWorkerTests
{
    [Fact]
    public async Task ExpireStaleProposalsAsync_ShouldLogSummaryWithoutExceptionObject_WhenProposalCannotBeExpired()
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Approved proposal should not be expired again",
            RiskLevel.Low,
            "proposal-housekeeping-redaction");
        proposal.Approve(Guid.NewGuid());
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-5));

        var repository = new FakeAutomationProposalRepository([proposal]);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
        var entry = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        entry.Exception.Should().BeNull();
        entry.Message.Should().Contain("Failed to expire proposal");
        entry.Message.Should().Contain("DomainException");
        entry.Message.Should().Contain("Cannot expire proposal in status Approved");
    }

    private static ServiceProvider BuildServiceProvider(IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        return services.BuildServiceProvider();
    }

    private static async Task InvokeExpireStaleProposalsAsync(
        ProposalHousekeepingWorker worker,
        CancellationToken cancellationToken)
    {
        var method = typeof(ProposalHousekeepingWorker).GetMethod(
            "ExpireStaleProposalsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var invocation = method!.Invoke(worker, [cancellationToken]);
        invocation.Should().NotBeNull();
        await (Task)invocation!;
    }

    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        var property = typeof(AutomationProposal).GetProperty(
            nameof(AutomationProposal.ExpiresAt),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.SetValue(proposal, expiresAt);
    }

    private sealed class FakeAutomationProposalRepository : IAutomationProposalRepository
    {
        private readonly IReadOnlyList<AutomationProposal> _proposals;

        public FakeAutomationProposalRepository(IReadOnlyList<AutomationProposal> proposals)
        {
            _proposals = proposals;
        }

        public Task<IEnumerable<AutomationProposal>> GetByStatusAsync(
            ProposalStatus status,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<AutomationProposal>>(_proposals.Take(limit).ToList());
        }

        public Task<int> CountPendingReviewByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_proposals.Count(proposal =>
                proposal.RequestedByUserId == userId &&
                proposal.Status == ProposalStatus.PendingReview));
        }

        public Task<bool> HasReviewedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_proposals.Any(proposal =>
                proposal.RequestedByUserId == userId &&
                proposal.Status is ProposalStatus.Approved
                    or ProposalStatus.Rejected
                    or ProposalStatus.Applied
                    or ProposalStatus.Failed
                    or ProposalStatus.Expired));
        }

        public Task<AutomationProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_proposals.SingleOrDefault(proposal => proposal.Id == id));
        }

        public Task<IEnumerable<AutomationProposal>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AutomationProposal> AddAsync(AutomationProposal entity, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task UpdateAsync(AutomationProposal entity, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(AutomationProposal entity, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AutomationProposal?> GetBySourceReferenceAsync(ProposalSourceType sourceType, string referenceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AutomationProposal?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AutomationProposal?> GetLatestByOperationTargetAsync(string targetType, string targetId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AutomationProposal?> GetLatestByOperationTargetAsync(
            string targetType,
            string targetId,
            string actionType,
            ProposalSourceType sourceType,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<AutomationProposal>> GetExpiredAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public FakeUnitOfWork(IAutomationProposalRepository automationProposalRepository)
        {
            AutomationProposals = automationProposalRepository;
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
        public IAutomationProposalRepository AutomationProposals { get; }
        public IArchiveItemRepository ArchiveItems => null!;
        public IChatSessionRepository ChatSessions => null!;
        public IChatMessageRepository ChatMessages => null!;
        public ICommandRunRepository CommandRuns => null!;
        public INotificationRepository Notifications => null!;
        public INotificationPreferenceRepository NotificationPreferences => null!;
        public IUserPreferenceRepository UserPreferences => null!;
        public IOutboundWebhookSubscriptionRepository OutboundWebhookSubscriptions => null!;
        public IOutboundWebhookDeliveryRepository OutboundWebhookDeliveries => null!;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
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
