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

    [Fact]
    public async Task ExpireStaleProposalsAsync_ShouldNotExpire_DeferredProposalWhoseOriginalExpiryHasPassed()
    {
        // A proposal that would have expired in 10 minutes, then snoozed for an hour:
        // Defer pushes ExpiresAt well beyond now, so the next housekeeping cycle must
        // leave it PendingReview (the snooze keeps it alive).
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Snoozed near-expiry proposal stays alive",
            RiskLevel.Low,
            "proposal-housekeeping-defer",
            expiryMinutes: 10);
        proposal.Defer(TimeSpan.FromHours(1));

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

        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        proposal.DeferredUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task ExpireStaleProposalsAsync_ShouldNotExpireWithheldProposals_AndShouldLogTheArchivedBoardSkip()
    {
        // #2197: the sweep hands the worker only what it may touch. Proposals on an archived board
        // arrive as a COUNT, never as entities, so the worker cannot expire one even by accident —
        // and it says how many it declined rather than letting them vanish silently.
        var expirable = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Active-board proposal expires normally",
            RiskLevel.Low,
            "proposal-housekeeping-active-board",
            expiryMinutes: 1);
        SetExpiresAt(expirable, DateTime.UtcNow.AddMinutes(-5));

        var repository = new FakeAutomationProposalRepository([expirable])
        {
            SkippedArchivedBoardCount = 2
        };
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        expirable.Status.Should().Be(
            ProposalStatus.Expired,
            "the active-board sibling must still expire — the guard must not stall the whole sweep");

        var skipEntry = logger.Entries.Should()
            .ContainSingle(entry =>
                entry.Level == LogLevel.Information && entry.Message.Contains("Skipped expiring"))
            .Subject;
        skipEntry.Message.Should().Contain("2");
        skipEntry.Message.Should().Contain("board is archived");
        // Non-secret: a count and a remediation hint, never an id, summary, or board name.
        skipEntry.Message.Should().NotContain(expirable.Id.ToString());
    }

    [Fact]
    public async Task ExpireStaleProposalsAsync_ShouldNotLogTheArchivedBoardSkip_WhenNothingWasWithheld()
    {
        // The line is an exception report, not a per-cycle heartbeat: a worker that logged it every
        // minute on a healthy install would train operators to ignore it.
        var expirable = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Nothing withheld this cycle",
            RiskLevel.Low,
            "proposal-housekeeping-no-skip",
            expiryMinutes: 1);
        SetExpiresAt(expirable, DateTime.UtcNow.AddMinutes(-5));

        var repository = new FakeAutomationProposalRepository([expirable]);
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        logger.Entries.Should().NotContain(entry => entry.Message.Contains("Skipped expiring"));
    }

    [Fact]
    public async Task ExpireStaleProposalsAsync_ShouldLogTheArchivedBoardSkipOnce_WhenTheCountIsUnchangedAcrossSweeps()
    {
        // The sweep runs every 60s and a withheld proposal stays PendingReview until its board is
        // restored, so an unconditional log emitted ~1,440 identical lines a day for one archived
        // board. The steady state must be Debug; only a CHANGE earns an Information line.
        var repository = new FakeAutomationProposalRepository([])
        {
            SkippedArchivedBoardCount = 2
        };
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        logger.Entries.Should().ContainSingle(
            entry => entry.Level == LogLevel.Information && entry.Message.Contains("Skipped expiring"),
            "two sweeps reporting the same withheld count must not repeat the Information line");
        logger.Entries.Should().ContainSingle(
            entry => entry.Level == LogLevel.Debug && entry.Message.Contains("Still skipping"),
            "the unchanged steady state is still recorded, just at Debug");
    }

    [Fact]
    public async Task ExpireStaleProposalsAsync_ShouldLogAgain_WhenTheArchivedBoardCountChanges()
    {
        // The counterpart to the test above: suppression must be keyed on the VALUE, not on
        // "already logged once", or a growing archived backlog would go unreported.
        var repository = new FakeAutomationProposalRepository([])
        {
            SkippedArchivedBoardCount = 2
        };
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        repository.SkippedArchivedBoardCount = 5;
        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        var informational = logger.Entries
            .Where(entry => entry.Level == LogLevel.Information && entry.Message.Contains("Skipped expiring"))
            .ToList();
        informational.Should().HaveCount(2);
        informational[0].Message.Should().Contain("2");
        informational[1].Message.Should().Contain("5");
    }

    [Fact]
    public async Task ExpireStaleProposalsAsync_ShouldReportRecovery_WhenTheArchivedBoardCountReturnsToZero()
    {
        // An operator who saw the skip line needs to see it clear; otherwise the only way to learn
        // the backlog is gone is the absence of a line that was already suppressed.
        var repository = new FakeAutomationProposalRepository([])
        {
            SkippedArchivedBoardCount = 3
        };
        var unitOfWork = new FakeUnitOfWork(repository);
        using var serviceProvider = BuildServiceProvider(unitOfWork);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        repository.SkippedArchivedBoardCount = 0;
        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        logger.Entries.Should().ContainSingle(
            entry => entry.Message.Contains("No stale proposals are being withheld"),
            "the recovery is announced once, not on every later clean sweep");
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

        public Task<IReadOnlyList<AutomationProposal>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            var requestedIds = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<AutomationProposal>>(
                _proposals.Where(proposal => requestedIds.Contains(proposal.Id)).ToList());
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
                proposal.DecidedByUserId == userId &&
                proposal.Status is ProposalStatus.Approved
                    or ProposalStatus.Rejected
                    or ProposalStatus.Applied
                    or ProposalStatus.Failed));
        }

        public Task<bool> HasAppliedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_proposals.Any(proposal =>
                proposal.DecidedByUserId == userId &&
                proposal.Status == ProposalStatus.Applied));
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

        public Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, bool includeDeferred = false, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<AutomationProposal>> GetActiveByUserIdAsync(Guid userId, int limit = 100, ProposalStatus? status = null, RiskLevel? riskLevel = null, CancellationToken cancellationToken = default)
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

        public Task<IReadOnlyList<AutomationProposal>> GetPendingByOperationTargetAsync(
            string targetType,
            string targetId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ExpiredProposalSweep> GetExpiredAsync(CancellationToken cancellationToken = default)
        {
            // Mirror the real query's ExpiresAt filter; intentionally ignore status (like GetByStatusAsync
            // above) so a non-PendingReview proposal still reaches the worker's Expire() catch path.
            // The archived-board partition is the real repository's job (#2197); this fake holds no
            // boards, so it reports nothing withheld unless a test overrides SkippedArchivedBoardCount.
            return Task.FromResult(new ExpiredProposalSweep(
                _proposals.Where(p => p.ExpiresAt < DateTime.UtcNow).ToList(),
                SkippedArchivedBoardCount));
        }

        /// <summary>
        /// Lets a test drive the worker's "withheld" log line without a database, and change it
        /// between sweeps to exercise the transition-only logging.
        /// </summary>
        public int SkippedArchivedBoardCount { get; set; }

        public Task<IReadOnlyList<AutomationProposal>> GetTerminalByActionTypeAsync(string actionType, Guid? boardId, Guid userId, int limit = 100, CancellationToken cancellationToken = default)
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
        public ILlmUsageRecordRepository LlmUsageRecords => null!;
        public IAgentProfileRepository AgentProfiles => null!;
        public IAgentRunRepository AgentRuns => null!;
        public IKnowledgeDocumentRepository KnowledgeDocuments => null!;
        public IKnowledgeChunkRepository KnowledgeChunks => null!;
        public IExternalLoginRepository ExternalLogins => null!;
        public IOAuthAuthCodeRepository OAuthAuthCodes => null!;
        public IApiKeyRepository ApiKeys => null!;
        public IMfaCredentialRepository MfaCredentials => null!;
        public IIntegrationConnectorRepository IntegrationConnectors => null!;
        public IConnectorEventRepository ConnectorEvents => null!;
        public IConnectorCredentialRepository ConnectorCredentials => null!;
        public IProposalRevisionRepository ProposalRevisions => null!;
        public IProposalFeedbackRepository ProposalFeedbacks => null!;
        public IDailySnapshotRepository DailySnapshots => null!;
        public ITomorrowNoteRepository TomorrowNotes => null!;
        public IMcpToolHashRepository McpToolHashes => null!;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task CheckpointWalAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
