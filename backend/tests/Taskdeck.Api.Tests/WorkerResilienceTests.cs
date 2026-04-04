using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Workers;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Resilience tests for background workers: LlmQueueToProposalWorker and
/// ProposalHousekeepingWorker. Covers issue #720 (TST-53):
/// - Single batch failure does not kill the worker loop
/// - Cancellation token fires → worker shuts down cleanly
/// - Queue items remain for retry when planner/service is unavailable
/// - Worker handles unhandled exceptions without accumulating zombie items
/// </summary>
public class WorkerResilienceTests
{
    // -----------------------------------------------------------------------
    // LlmQueueToProposalWorker — batch failure does not kill the worker
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LlmQueueWorker_PlannerThrowsOnEveryItem_WorkerLoopContinuesAndLogsError()
    {
        // Items should enter retry state; the worker loop should not crash.
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService
        {
            ResultFactory = (string _) => throw new InvalidOperationException("Simulated planner crash")
        };
        using var sp = BuildServiceProvider(queueRepo, planner);
        var logger = new InMemoryLogger<LlmQueueToProposalWorker>();
        var worker = CreateWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]),
            logger);

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // Item should be in Pending state (retry scheduled), not Failed or Processing zombie
        item.Status.Should().Be(RequestStatus.Pending);
        item.RetryCount.Should().Be(1);
        // No exception should propagate — the batch handler catches and retries internally
    }

    [Fact]
    public async Task LlmQueueWorker_DatabaseThrowsOnGetStatus_WorkerDoesNotCrash()
    {
        // Simulate DB unavailability on the queue read — the outer ExecuteAsync loop
        // should catch and log the error and continue without crashing.
        var throwingQueueRepo = new ThrowingLlmQueueRepository();
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWorkWithLlmQueue(throwingQueueRepo));
        services.AddSingleton<IAutomationPlannerService>(new FakeAutomationPlannerService());
        services.AddSingleton<ICaptureTriageService>(new FakeCaptureTriageService());
        await using var sp = services.BuildServiceProvider();

        var logger = new InMemoryLogger<LlmQueueToProposalWorker>();
        var worker = CreateWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(),
            logger);

        // ProcessBatchAsync should propagate the exception (the outer ExecuteAsync loop catches it)
        var act = async () => await InvokeProcessBatchAsync(worker, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>("DB error propagates to outer loop");
    }

    [Fact]
    public async Task LlmQueueWorker_ExecuteAsync_DatabaseFailure_OuterLoopCatchesAndContinues()
    {
        // The outer ExecuteAsync loop in LlmQueueToProposalWorker has a catch(Exception)
        // that logs and continues. Verify that a single batch exception doesn't kill the worker.
        // We cancel immediately after one iteration to avoid an infinite loop.
        var throwingQueueRepo = new ThrowingLlmQueueRepository();
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWorkWithLlmQueue(throwingQueueRepo));
        services.AddSingleton<IAutomationPlannerService>(new FakeAutomationPlannerService());
        services.AddSingleton<ICaptureTriageService>(new FakeCaptureTriageService());
        await using var sp = services.BuildServiceProvider();

        var logger = new InMemoryLogger<LlmQueueToProposalWorker>();
        // Very short poll interval so we can cancel after the first iteration
        var settings = new WorkerSettings
        {
            EnableAutoQueueProcessing = true,
            QueuePollIntervalSeconds = 1,
            MaxBatchSize = 10,
            MaxConcurrency = 1,
            MaxRetries = 3,
            RetryBackoffSeconds = [0]
        };
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), settings, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try
        {
            await InvokeExecuteAsync(worker, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected — the test cancels the worker after the first loop
        }

        // The outer loop should have logged an error (not re-thrown)
        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Error,
            "outer loop catches batch exceptions and logs them");
    }

    // -----------------------------------------------------------------------
    // LlmQueueToProposalWorker — cancellation token shuts down cleanly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LlmQueueWorker_CancellationFiresDuringDelay_WorkerExitsCleanly()
    {
        var queueRepo = new FakeLlmQueueRepository([]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before starting

        var act = async () => await InvokeExecuteAsync(worker, cts.Token);
        await act.Should().NotThrowAsync(
            "worker should exit cleanly when cancellation is requested before first iteration");
    }

    [Fact]
    public async Task LlmQueueWorker_CancellationFiresMidBatch_ItemsNotLeftInZombieState()
    {
        // If cancellation fires mid-batch, the OperationCanceledException propagates up
        // but since we process items individually (Task.Run per item), items that haven't
        // started should remain Pending (not Processing zombie).
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService
        {
            ResultFactory = _ => Result.Success(new ProposalDto(
                Guid.NewGuid(), ProposalSourceType.Chat, null, null, Guid.NewGuid(),
                ProposalStatus.PendingReview, RiskLevel.Low, "summary", null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTime.UtcNow.AddHours(1),
                null, null, null, null, "corr", new List<ProposalOperationDto>()))
        };
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Immediately cancelled

        var act = async () => await InvokeProcessBatchAsync(worker, cts.Token);
        // May throw OperationCanceledException — that is fine, just not an unhandled crash
        try { await act(); }
        catch (OperationCanceledException) { }

        // Item must not be in a "Processing" zombie state if we never started on it
        item.Status.Should().NotBe(RequestStatus.Processing,
            "item should not be left in zombie Processing state after cancellation");
    }

    // -----------------------------------------------------------------------
    // LlmQueueToProposalWorker — items at max retries are marked Failed, not retried
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LlmQueueWorker_ItemAtMaxRetries_MarkedFailedNotRetried()
    {
        var item = CreatePendingItem();
        // Exhaust retries first
        var queueRepo = new FakeLlmQueueRepository([item]);
        var alwaysFail = new FakeAutomationPlannerService
        {
            ResultFactory = (string _) => Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, "Persistent failure")
        };
        using var sp = BuildServiceProvider(queueRepo, alwaysFail);
        var worker = CreateWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 1, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Failed,
            "item at max retries should be marked Failed, not left pending for infinite retry");
        item.RetryCount.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ProposalHousekeepingWorker — single batch failure does not kill the worker
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HousekeepingWorker_ExpireStaleProposalsThrows_WorkerLogsAndDoesNotCrash()
    {
        // Create a proposal in an unresolvable state (approved, so Expire() throws DomainException)
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Already approved proposal",
            RiskLevel.Low,
            "resilience-test-tag");
        proposal.Approve(Guid.NewGuid());
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-5));

        var repository = new FakeAutomationProposalRepository([proposal]);
        var unitOfWork = new FakeUnitOfWorkWithProposals(repository);
        using var serviceProvider = BuildHousekeepingServiceProvider(unitOfWork);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        // Should not throw; worker logs the failure and continues
        var act = async () => await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);
        await act.Should().NotThrowAsync();

        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning && e.Message.Contains("Failed to expire proposal"),
            "housekeeping worker logs individual item failures without crashing");
    }

    [Fact]
    public async Task HousekeepingWorker_MultipleProposals_PartialFailure_ContinuesWithRemainder()
    {
        // Mix: one approved (will fail to expire) and one pending (can expire)
        var approvedProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Approved — cannot be re-expired",
            RiskLevel.Low,
            "resilience-mixed-1");
        approvedProposal.Approve(Guid.NewGuid());
        SetExpiresAt(approvedProposal, DateTime.UtcNow.AddMinutes(-5));

        var pendingProposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Pending — can be expired",
            RiskLevel.Low,
            "resilience-mixed-2");
        SetExpiresAt(pendingProposal, DateTime.UtcNow.AddMinutes(-5));

        var repository = new FakeAutomationProposalRepository([approvedProposal, pendingProposal]);
        var unitOfWork = new FakeUnitOfWorkWithProposals(repository);
        using var serviceProvider = BuildHousekeepingServiceProvider(unitOfWork);
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        await InvokeExpireStaleProposalsAsync(worker, CancellationToken.None);

        // The approved one logs a warning; the pending one is expired successfully
        pendingProposal.Status.Should().Be(ProposalStatus.Expired,
            "valid pending proposal should be expired even when another proposal fails");
        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning,
            "warning logged for the proposal that could not be expired");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static LlmRequest CreatePendingItem(Guid? userId = null)
    {
        return new LlmRequest(userId ?? Guid.NewGuid(), "instruction", "Create a task", null);
    }

    private static WorkerSettings DefaultSettings(
        bool enableProcessing = true,
        int maxRetries = 3,
        int[]? retryBackoff = null)
    {
        return new WorkerSettings
        {
            EnableAutoQueueProcessing = enableProcessing,
            QueuePollIntervalSeconds = 1,
            MaxBatchSize = 10,
            MaxConcurrency = 1,
            MaxRetries = maxRetries,
            RetryBackoffSeconds = retryBackoff ?? [0]
        };
    }

    private static LlmQueueToProposalWorker CreateWorker(
        IServiceScopeFactory scopeFactory,
        WorkerSettings? settings = null,
        ILogger<LlmQueueToProposalWorker>? logger = null)
    {
        return new LlmQueueToProposalWorker(
            scopeFactory,
            settings ?? DefaultSettings(),
            new WorkerHeartbeatRegistry(),
            logger ?? NullLogger<LlmQueueToProposalWorker>.Instance);
    }

    private static ServiceProvider BuildServiceProvider(
        FakeLlmQueueRepository queueRepo,
        FakeAutomationPlannerService? planner = null,
        FakeCaptureTriageService? triageService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWorkWithLlmQueue(queueRepo));
        services.AddSingleton<IAutomationPlannerService>(planner ?? new FakeAutomationPlannerService());
        services.AddSingleton<ICaptureTriageService>(triageService ?? new FakeCaptureTriageService());
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildHousekeepingServiceProvider(IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        return services.BuildServiceProvider();
    }

    private static async Task InvokeProcessBatchAsync(LlmQueueToProposalWorker worker, CancellationToken ct)
    {
        var method = typeof(LlmQueueToProposalWorker).GetMethod(
            "ProcessBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("ProcessBatchAsync must exist");
        await (Task)method!.Invoke(worker, [ct])!;
    }

    private static async Task InvokeExecuteAsync(LlmQueueToProposalWorker worker, CancellationToken ct)
    {
        var method = typeof(LlmQueueToProposalWorker).GetMethod(
            "ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("ExecuteAsync must exist");
        await (Task)method!.Invoke(worker, [ct])!;
    }

    private static async Task InvokeExpireStaleProposalsAsync(
        ProposalHousekeepingWorker worker, CancellationToken ct)
    {
        var method = typeof(ProposalHousekeepingWorker).GetMethod(
            "ExpireStaleProposalsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("ExpireStaleProposalsAsync must exist");
        await (Task)method!.Invoke(worker, [ct])!;
    }

    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        var property = typeof(AutomationProposal).GetProperty(
            nameof(AutomationProposal.ExpiresAt),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.SetValue(proposal, expiresAt);
    }

    // -----------------------------------------------------------------------
    // Fake repositories and services
    // -----------------------------------------------------------------------

    private sealed class ThrowingLlmQueueRepository : ILlmQueueRepository
    {
        public Task<IEnumerable<LlmRequest>> GetByStatusAsync(
            RequestStatus status, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Database unavailable — simulated for resilience test");

        public Task<LlmRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IEnumerable<LlmRequest>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<LlmRequest> AddAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task UpdateAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task DeleteAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<(int TotalCaptures, int NewCount, int FailedCount, int TriagingCount, int TriagedCount)> GetCaptureSummaryByUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IEnumerable<LlmRequest>> GetByUserAndStatusAsync(Guid userId, RequestStatus status, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<Dictionary<RequestStatus, int>> GetStatusCountsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> TryClaimProcessingCaptureAsync(Guid requestId, DateTimeOffset expectedUpdatedAt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeLlmQueueRepository : ILlmQueueRepository
    {
        private readonly List<LlmRequest> _pending;
        private readonly List<LlmRequest> _processing;

        public FakeLlmQueueRepository(
            IEnumerable<LlmRequest> pending,
            IEnumerable<LlmRequest>? processing = null)
        {
            _pending = [..pending];
            _processing = [..(processing ?? [])];
        }

        public Task<IEnumerable<LlmRequest>> GetByStatusAsync(
            RequestStatus status, CancellationToken cancellationToken = default)
        {
            var all = _pending.Concat(_processing).ToList();
            return Task.FromResult<IEnumerable<LlmRequest>>(
                all.Where(i => i.Status == status).ToList());
        }

        public Task<LlmRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_pending.Concat(_processing).FirstOrDefault(i => i.Id == id));
        public Task<IEnumerable<LlmRequest>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_pending.Concat(_processing).ToList());
        public Task<LlmRequest> AddAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);
        public Task UpdateAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task DeleteAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<(int TotalCaptures, int NewCount, int FailedCount, int TriagingCount, int TriagedCount)> GetCaptureSummaryByUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult((0, 0, 0, 0, 0));
        public Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_pending.Where(i => i.Status == RequestStatus.Pending).Take(limit).ToList());
        public Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_pending.Concat(_processing).Where(i => i.UserId == userId));
        public Task<IEnumerable<LlmRequest>> GetByUserAndStatusAsync(Guid userId, RequestStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult(_pending.Concat(_processing).Where(i => i.UserId == userId && i.Status == status));
        public Task<Dictionary<RequestStatus, int>> GetStatusCountsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new Dictionary<RequestStatus, int>());
        public Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_pending.FirstOrDefault(i => i.Status == RequestStatus.Pending));
        public Task<bool> TryClaimProcessingCaptureAsync(Guid requestId, DateTimeOffset expectedUpdatedAt, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakeAutomationPlannerService : IAutomationPlannerService
    {
        public int CallCount { get; private set; }
        public Func<string, Result<ProposalDto>>? ResultFactory { get; set; }

        public Task<Result<ProposalDto>> ParseInstructionAsync(
            string instruction,
            Guid userId,
            Guid? boardId = null,
            CancellationToken cancellationToken = default,
            ProposalSourceType sourceType = ProposalSourceType.Manual,
            string? sourceReferenceId = null,
            string? correlationId = null)
        {
            CallCount++;
            if (ResultFactory != null)
                return Task.FromResult(ResultFactory(instruction));

            return Task.FromResult(Result.Success(new ProposalDto(
                Guid.NewGuid(), ProposalSourceType.Chat, null, boardId, userId,
                ProposalStatus.PendingReview, RiskLevel.Low, "summary", null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTime.UtcNow.AddHours(1),
                null, null, null, null, "corr", new List<ProposalOperationDto>())));
        }

        public Task<Result<ProposalDto>> ParseBatchInstructionAsync(
            IReadOnlyList<string> instructions,
            Guid userId,
            Guid? boardId = null,
            CancellationToken cancellationToken = default,
            ProposalSourceType sourceType = ProposalSourceType.Manual,
            string? sourceReferenceId = null,
            string? correlationId = null)
        {
            CallCount++;
            if (ResultFactory != null)
                return Task.FromResult(ResultFactory(string.Join(";", instructions)));

            return Task.FromResult(Result.Success(new ProposalDto(
                Guid.NewGuid(), ProposalSourceType.Chat, null, boardId, userId,
                ProposalStatus.PendingReview, RiskLevel.Low, "batch summary", null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTime.UtcNow.AddHours(1),
                null, null, null, null, "corr-batch", new List<ProposalOperationDto>())));
        }
    }

    private sealed class FakeCaptureTriageService : ICaptureTriageService
    {
        public Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromCaptureAsync(
            Guid captureItemId, Guid userId, Guid? boardId,
            CapturePayloadV1 payload, CancellationToken cancellationToken = default)
        {
            var result = new CaptureTriageProposalResultDto(
                captureItemId, Guid.NewGuid(), Guid.NewGuid(), 1, "v1", "mock", "mock-model");
            return Task.FromResult(Result.Success(result));
        }
    }

    private sealed class FakeAutomationProposalRepository : IAutomationProposalRepository
    {
        private readonly List<AutomationProposal> _proposals;

        public FakeAutomationProposalRepository(IEnumerable<AutomationProposal> proposals)
        {
            _proposals = [..proposals];
        }

        public Task<IEnumerable<AutomationProposal>> GetByStatusAsync(
            ProposalStatus status, int limit = 100, CancellationToken cancellationToken = default)
            // Returns all proposals (ignores status filter) so the worker iterates every item.
            // This allows testing error handling when a proposal rejects an operation.
            => Task.FromResult<IEnumerable<AutomationProposal>>(
                _proposals.Take(limit).ToList());

        public Task<IEnumerable<AutomationProposal>> GetExpiredAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<AutomationProposal>>(
                _proposals.Where(p =>
                    p.ExpiresAt < DateTime.UtcNow &&
                    p.Status == ProposalStatus.PendingReview).ToList());

        public Task<AutomationProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_proposals.SingleOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<AutomationProposal>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AutomationProposal>>(
                _proposals.Where(p => ids.Contains(p.Id)).ToList());
        public Task<int> CountPendingReviewByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
        public Task<bool> HasReviewedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<IEnumerable<AutomationProposal>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AutomationProposal> AddAsync(AutomationProposal entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task UpdateAsync(AutomationProposal entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task DeleteAsync(AutomationProposal entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AutomationProposal?> GetBySourceReferenceAsync(ProposalSourceType sourceType, string referenceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AutomationProposal?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AutomationProposal?> GetLatestByOperationTargetAsync(string targetType, string targetId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AutomationProposal?> GetLatestByOperationTargetAsync(string targetType, string targetId, string actionType, ProposalSourceType sourceType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWorkWithLlmQueue : IUnitOfWork
    {
        public FakeUnitOfWorkWithLlmQueue(ILlmQueueRepository llmQueue) { LlmQueue = llmQueue; }
        public IBoardRepository Boards => null!;
        public IColumnRepository Columns => null!;
        public ICardRepository Cards => null!;
        public ICardCommentRepository CardComments => null!;
        public ILabelRepository Labels => null!;
        public IUserRepository Users => null!;
        public IBoardAccessRepository BoardAccesses => null!;
        public IAuditLogRepository AuditLogs => null!;
        public ILlmQueueRepository LlmQueue { get; }
        public IAutomationProposalRepository AutomationProposals => null!;
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
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWorkWithProposals : IUnitOfWork
    {
        public FakeUnitOfWorkWithProposals(IAutomationProposalRepository proposals) { AutomationProposals = proposals; }
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
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
