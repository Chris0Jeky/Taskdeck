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

/// <summary>
/// Edge-case tests for ProposalHousekeepingWorker covering batch expiry,
/// mixed-state proposals, cancellation, database error resilience,
/// and race conditions between worker and manual actions.
/// Addresses issue #708 (TST-41) scenario 20.
/// </summary>
public class ProposalHousekeepingWorkerEdgeCaseTests
{
    #region Normal Expiry of Pending Proposals

    [Fact]
    public async Task ExpireStaleProposals_ShouldExpirePendingProposal_WhenPastExpiresAt()
    {
        var proposal = CreatePendingProposal();
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));

        var repository = new TrackingProposalRepository([proposal]);
        var unitOfWork = new FakeUnitOfWork(repository);
        var (worker, sp) = CreateWorkerWithProvider(unitOfWork);
        using (sp)
        {
            await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
        }

        proposal.Status.Should().Be(ProposalStatus.Expired);
        repository.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ExpireStaleProposals_ShouldNotExpirePendingProposal_WhenNotYetExpired()
    {
        var proposal = CreatePendingProposal();

        var repository = new TrackingProposalRepository([proposal]);
        var unitOfWork = new FakeUnitOfWork(repository);
        var (worker, sp) = CreateWorkerWithProvider(unitOfWork);
        using (sp)
        {
            await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
        }

        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        repository.SaveChangesCalled.Should().BeFalse();
    }

    #endregion

    #region Batch Expiry with Mixed States

    [Fact]
    public async Task ExpireStaleProposals_ShouldHandleMixedStates_ExpiringOnlyEligiblePending()
    {
        var expiredPending = CreatePendingProposal();
        SetExpiresAt(expiredPending, DateTime.UtcNow.AddMinutes(-5));

        var freshPending = CreatePendingProposal();

        var repository = new TrackingProposalRepository([expiredPending, freshPending]);
        var unitOfWork = new FakeUnitOfWork(repository);
        var (worker, sp) = CreateWorkerWithProvider(unitOfWork);
        using (sp)
        {
            await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
        }

        expiredPending.Status.Should().Be(ProposalStatus.Expired);
        freshPending.Status.Should().Be(ProposalStatus.PendingReview);
        repository.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ExpireStaleProposals_ShouldExpireAllExpiredInBatch()
    {
        var proposals = Enumerable.Range(0, 50)
            .Select(_ =>
            {
                var p = CreatePendingProposal();
                SetExpiresAt(p, DateTime.UtcNow.AddMinutes(-1));
                return p;
            })
            .ToList();

        var repository = new TrackingProposalRepository(proposals);
        var unitOfWork = new FakeUnitOfWork(repository);
        var (worker, sp) = CreateWorkerWithProvider(unitOfWork);
        using (sp)
        {
            await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
        }

        proposals.Should().AllSatisfy(p => p.Status.Should().Be(ProposalStatus.Expired));
        repository.SaveChangesCalled.Should().BeTrue();
    }

    #endregion

    #region Database Error Resilience

    [Fact]
    public async Task ExpireStaleProposals_ShouldPropagateDbError_WhenGetByStatusThrows()
    {
        var repository = new ThrowingProposalRepository();
        var unitOfWork = new FakeUnitOfWork(repository);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var (worker, sp) = CreateWorkerWithProvider(unitOfWork, logger);
        using (sp)
        {
            // ExpireStaleProposalsAsync doesn't catch DB errors in the fetch path;
            // the ExecuteAsync loop handles that. Verify it propagates cleanly.
            var act = () => InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    #endregion

    #region Race: Worker vs Manual Approval

    [Fact]
    public async Task ExpireStaleProposals_ShouldLogWarning_WhenProposalWasApprovedBetweenFetchAndExpire()
    {
        // Simulate race: proposal fetched as PendingReview but was approved
        // before the worker iterates to it. Approve first (while not expired),
        // then set ExpiresAt to past (simulating time passing).
        var proposal = CreatePendingProposal();
        proposal.Approve(Guid.NewGuid());
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-5));

        var repository = new TrackingProposalRepository([proposal]);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var unitOfWork = new FakeUnitOfWork(repository);
        var (worker, sp) = CreateWorkerWithProvider(unitOfWork, logger);
        using (sp)
        {
            await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
        }

        proposal.Status.Should().Be(ProposalStatus.Approved, "proposal should remain approved");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
        logger.Entries.Single(e => e.Level == LogLevel.Warning)
            .Message.Should().Contain("Failed to expire proposal");
    }

    #endregion

    #region Worker ExecuteAsync Loop

    [Fact]
    public async Task ExecuteAsync_ShouldStopWithinReasonableTime_WhenCancelled()
    {
        var repository = new TrackingProposalRepository([]);
        var unitOfWork = new FakeUnitOfWork(repository);
        var (worker, sp) = CreateWorkerWithProvider(unitOfWork);
        using (sp)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            var executeMethod = typeof(ProposalHousekeepingWorker).GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            executeMethod.Should().NotBeNull();

            // Task.Delay throws TaskCanceledException when the token fires
            // during the sleep. This is expected behavior for BackgroundService.
            var task = (Task)executeMethod!.Invoke(worker, [cts.Token])!;
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TaskCanceledException)
            {
                // Expected: Task.Delay cancellation propagates
            }

            task.IsCompleted.Should().BeTrue("worker should stop promptly after cancellation");
        }
    }

    #endregion

    #region Helpers

    private static AutomationProposal CreatePendingProposal()
    {
        return new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
    }

    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        typeof(AutomationProposal).GetProperty(
            nameof(AutomationProposal.ExpiresAt),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(proposal, expiresAt);
    }

    private static (ProposalHousekeepingWorker Worker, ServiceProvider ServiceProvider) CreateWorkerWithProvider(
        IUnitOfWork unitOfWork,
        ILogger<ProposalHousekeepingWorker>? logger = null)
    {
        var sp = BuildServiceProvider(unitOfWork);
        var worker = new ProposalHousekeepingWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger ?? new InMemoryLogger<ProposalHousekeepingWorker>());
        return (worker, sp);
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
        await (Task)method!.Invoke(worker, [cancellationToken])!;
    }

    #endregion

    #region Test Doubles

    private sealed class TrackingProposalRepository : IAutomationProposalRepository
    {
        private readonly IReadOnlyList<AutomationProposal> _proposals;
        public bool SaveChangesCalled { get; set; }

        public TrackingProposalRepository(IReadOnlyList<AutomationProposal> proposals)
        {
            _proposals = proposals;
        }

        public Task<IEnumerable<AutomationProposal>> GetByStatusAsync(
            ProposalStatus status, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<AutomationProposal>>(_proposals.Take(limit).ToList());

        public Task<IReadOnlyList<AutomationProposal>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountPendingReviewByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasReviewedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal> AddAsync(AutomationProposal entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(AutomationProposal entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(AutomationProposal entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetBySourceReferenceAsync(ProposalSourceType sourceType, string referenceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetLatestByOperationTargetAsync(string targetType, string targetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetLatestByOperationTargetAsync(string targetType, string targetId, string actionType, ProposalSourceType sourceType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetExpiredAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingProposalRepository : IAutomationProposalRepository
    {
        public Task<IEnumerable<AutomationProposal>> GetByStatusAsync(
            ProposalStatus status, int limit = 100, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Database connection failed");

        public Task<IReadOnlyList<AutomationProposal>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountPendingReviewByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasReviewedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal> AddAsync(AutomationProposal entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(AutomationProposal entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(AutomationProposal entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetBySourceReferenceAsync(ProposalSourceType sourceType, string referenceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetLatestByOperationTargetAsync(string targetType, string targetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomationProposal?> GetLatestByOperationTargetAsync(string targetType, string targetId, string actionType, ProposalSourceType sourceType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetExpiredAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly TrackingProposalRepository? _trackingRepo;

        public FakeUnitOfWork(IAutomationProposalRepository repo)
        {
            AutomationProposals = repo;
            _trackingRepo = repo as TrackingProposalRepository;
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
        public ILlmUsageRecordRepository LlmUsageRecords => null!;
        public IAgentProfileRepository AgentProfiles => null!;
        public IAgentRunRepository AgentRuns => null!;
        public IKnowledgeDocumentRepository KnowledgeDocuments => null!;
        public IKnowledgeChunkRepository KnowledgeChunks => null!;
        public IExternalLoginRepository ExternalLogins => null!;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_trackingRepo != null) _trackingRepo.SaveChangesCalled = true;
            return Task.FromResult(0);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    #endregion
}
