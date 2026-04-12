using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Workers;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LlmQueueToProposalWorkerTests
{
    #region Helper factories

    private static LlmRequest CreatePendingItem(Guid? userId = null, Guid? boardId = null, string payload = "Create a task")
    {
        return new LlmRequest(
            userId ?? Guid.NewGuid(),
            "instruction",
            payload,
            boardId);
    }

    private static LlmRequest CreateCaptureTriageItem(
        Guid? userId = null,
        Guid? boardId = null,
        string? payload = null,
        Guid? existingProposalId = null)
    {
        var capturePayload = payload ?? BuildCapturePayloadJson(existingProposalId: existingProposalId);
        var item = new LlmRequest(
            userId ?? Guid.NewGuid(),
            CaptureRequestContract.RequestTypeV1,
            capturePayload,
            boardId);
        // Capture triage items are in Processing status
        item.MarkAsProcessing();
        return item;
    }

    private static string BuildCapturePayloadJson(
        string text = "Buy groceries",
        Guid? existingProposalId = null)
    {
        var provenancePart = existingProposalId.HasValue
            ? $",\"provenance\":{{\"captureItemId\":\"{Guid.NewGuid()}\",\"proposalId\":\"{existingProposalId.Value}\"}}"
            : "";
        return $"{{\"version\":1,\"source\":\"typed\",\"text\":\"{text}\"{provenancePart}}}";
    }

    private static WorkerSettings DefaultSettings(
        bool enableProcessing = true,
        int maxBatchSize = 10,
        int maxConcurrency = 1,
        int maxRetries = 3,
        int[]? retryBackoff = null)
    {
        return new WorkerSettings
        {
            EnableAutoQueueProcessing = enableProcessing,
            QueuePollIntervalSeconds = 1,
            MaxBatchSize = maxBatchSize,
            MaxConcurrency = maxConcurrency,
            MaxRetries = maxRetries,
            RetryBackoffSeconds = retryBackoff ?? [0]
        };
    }

    private static LlmQueueToProposalWorker CreateWorker(
        IServiceScopeFactory scopeFactory,
        WorkerSettings? settings = null)
    {
        return new LlmQueueToProposalWorker(
            scopeFactory,
            settings ?? DefaultSettings(),
            new WorkerHeartbeatRegistry(),
            NullLogger<LlmQueueToProposalWorker>.Instance);
    }

    private static ServiceProvider BuildServiceProvider(
        FakeLlmQueueRepository queueRepo,
        FakeAutomationPlannerService? planner = null,
        FakeCaptureTriageService? triageService = null)
    {
        var services = new ServiceCollection();
        var unitOfWork = new FakeUnitOfWork(queueRepo);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton<IAutomationPlannerService>(planner ?? new FakeAutomationPlannerService());
        services.AddSingleton<ICaptureTriageService>(triageService ?? new FakeCaptureTriageService());
        return services.BuildServiceProvider();
    }

    private static async Task InvokeProcessBatchAsync(
        LlmQueueToProposalWorker worker,
        CancellationToken ct)
    {
        var method = typeof(LlmQueueToProposalWorker).GetMethod(
            "ProcessBatchAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("ProcessBatchAsync must exist");
        var task = method!.Invoke(worker, [ct]);
        task.Should().NotBeNull();
        await (Task)task!;
    }

    #endregion

    #region Happy path: pending item processed to completion

    [Fact]
    public async Task ProcessBatch_PendingItem_SuccessfulPlanner_MarksCompleted()
    {
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService();
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed);
        planner.CallCount.Should().Be(1);
    }

    #endregion

    #region Empty queue

    [Fact]
    public async Task ProcessBatch_EmptyQueue_DoesNothing()
    {
        var queueRepo = new FakeLlmQueueRepository([]);
        var planner = new FakeAutomationPlannerService();
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        planner.CallCount.Should().Be(0);
    }

    #endregion

    #region Planner failure with retry

    [Fact]
    public async Task ProcessBatch_PlannerReturnsTransientError_RetriesItem()
    {
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService
        {
            ResultFactory = _ => Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, "Transient failure")
        };
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // After retry handler: item should be reset to Pending for retry
        item.Status.Should().Be(RequestStatus.Pending);
        item.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_PlannerReturnsTransientError_AtMaxRetries_FailsPermanently()
    {
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService
        {
            ResultFactory = _ => Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, "Transient failure")
        };
        using var sp = BuildServiceProvider(queueRepo, planner);
        // MaxRetries=1 means after first failure (RetryCount becomes 1), no more retries
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 1, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Failed);
        item.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_PlannerReturnsNonTransientError_FailsPermanently()
    {
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService
        {
            ResultFactory = _ => Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Bad input")
        };
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Failed);
    }

    #endregion

    #region Unhandled exception in planner

    [Fact]
    public async Task ProcessBatch_PlannerThrowsException_HandlesWithRetry()
    {
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService
        {
            ResultFactory = _ => throw new InvalidOperationException("Unexpected crash")
        };
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // Should still be recoverable since UnexpectedError is transient
        item.Status.Should().Be(RequestStatus.Pending);
        item.RetryCount.Should().Be(1);
    }

    #endregion

    #region Capture triage: happy path

    [Fact]
    public async Task ProcessBatch_CaptureTriageItem_SuccessfulTriage_MarksCompleted()
    {
        var item = CreateCaptureTriageItem();
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService: triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed);
        triageService.CallCount.Should().Be(1);
    }

    #endregion

    #region Capture triage: already linked

    [Fact]
    public async Task ProcessBatch_CaptureWithExistingProposalId_SkipsAndMarksCompleted()
    {
        var existingProposalId = Guid.NewGuid();
        var item = CreateCaptureTriageItem(existingProposalId: existingProposalId);
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService: triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed);
        triageService.CallCount.Should().Be(0, "triage should be skipped for already-linked captures");
    }

    #endregion

    #region Capture triage: failure with retry

    [Fact]
    public async Task ProcessBatch_CaptureTriageFailure_RetriesAsProcessing()
    {
        var item = CreateCaptureTriageItem();
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        var triageService = new FakeCaptureTriageService
        {
            ResultFactory = (_, _, _, _, _) =>
                Result.Failure<CaptureTriageProposalResultDto>(ErrorCodes.UnexpectedError, "Triage failed")
        };
        using var sp = BuildServiceProvider(queueRepo, triageService: triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // retryAsProcessing: true means item is reset to Processing, not Pending
        item.Status.Should().Be(RequestStatus.Processing);
        item.RetryCount.Should().Be(1);
    }

    #endregion

    #region Disabled processing

    [Fact]
    public async Task ExecuteAsync_DisabledProcessing_DoesNotProcessItems()
    {
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService();
        using var sp = BuildServiceProvider(queueRepo, planner);
        var settings = DefaultSettings(enableProcessing: false);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), settings);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await InvokeExecuteAsync(worker, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected: worker loops until cancelled
        }

        planner.CallCount.Should().Be(0);
        item.Status.Should().Be(RequestStatus.Pending);
    }

    private static async Task InvokeExecuteAsync(
        LlmQueueToProposalWorker worker,
        CancellationToken ct)
    {
        var method = typeof(LlmQueueToProposalWorker).GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("ExecuteAsync must exist");
        var task = method!.Invoke(worker, [ct]);
        task.Should().NotBeNull();
        await (Task)task!;
    }

    #endregion

    #region Worker lifecycle: cancellation during ExecuteAsync

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_StopsGracefully()
    {
        var queueRepo = new FakeLlmQueueRepository([]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Immediately cancel

        // Should not throw, just complete
        var act = async () => await InvokeExecuteAsync(worker, cts.Token);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region BuildFairBatchItems unit tests

    [Fact]
    public void BuildFairBatch_OnlyCaptureItems_ReturnsAllCapture()
    {
        var captures = new List<LlmRequest> { CreateCaptureTriageItem(), CreateCaptureTriageItem() };
        var pending = new List<LlmRequest>();

        var batch = InvokeBuildFairBatchItems(captures, pending, 10);

        batch.Should().HaveCount(2);
        batch.Should().OnlyContain(b => GetIsCaptureTriage(b));
    }

    [Fact]
    public void BuildFairBatch_OnlyPendingItems_ReturnsAllPending()
    {
        var captures = new List<LlmRequest>();
        var pending = new List<LlmRequest> { CreatePendingItem(), CreatePendingItem() };

        var batch = InvokeBuildFairBatchItems(captures, pending, 10);

        batch.Should().HaveCount(2);
        batch.Should().OnlyContain(b => !GetIsCaptureTriage(b));
    }

    [Fact]
    public void BuildFairBatch_MaxBatchSizeZero_ReturnsEmpty()
    {
        var captures = new List<LlmRequest> { CreateCaptureTriageItem() };
        var pending = new List<LlmRequest> { CreatePendingItem() };

        var batch = InvokeBuildFairBatchItems(captures, pending, 0);

        batch.Should().BeEmpty();
    }

    [Fact]
    public void BuildFairBatch_MaxBatchSizeOne_ReturnsSingleItem()
    {
        var captures = new List<LlmRequest> { CreateCaptureTriageItem() };
        var pending = new List<LlmRequest> { CreatePendingItem() };

        var batch = InvokeBuildFairBatchItems(captures, pending, 1);

        batch.Should().HaveCount(1);
    }

    [Fact]
    public void BuildFairBatch_MixedItems_CaptureOlder_InterleavesCapturePendingOrder()
    {
        var capture1 = CreateCaptureTriageItem();
        var capture2 = CreateCaptureTriageItem();
        var pending1 = CreatePendingItem();
        var pending2 = CreatePendingItem();

        // Set timestamps: captures are older than pending items
        SetCreatedAt(capture1, DateTimeOffset.UtcNow.AddMinutes(-10));
        SetCreatedAt(capture2, DateTimeOffset.UtcNow.AddMinutes(-9));
        SetCreatedAt(pending1, DateTimeOffset.UtcNow.AddMinutes(-5));
        SetCreatedAt(pending2, DateTimeOffset.UtcNow.AddMinutes(-4));

        var captures = new List<LlmRequest> { capture1, capture2 };
        var pending = new List<LlmRequest> { pending1, pending2 };

        var batch = InvokeBuildFairBatchItems(captures, pending, 10);

        batch.Should().HaveCount(4);
        // Captures are older, so takeCaptureFirst=true.
        // Interleaving order: capture, pending, capture, pending
        GetIsCaptureTriage(batch[0]).Should().BeTrue("first item should be capture (older)");
        GetIsCaptureTriage(batch[1]).Should().BeFalse("second item should be pending (interleaved)");
        GetIsCaptureTriage(batch[2]).Should().BeTrue("third item should be capture");
        GetIsCaptureTriage(batch[3]).Should().BeFalse("fourth item should be pending");
    }

    [Fact]
    public void BuildFairBatch_MixedItems_PendingOlder_InterleavesPendingCaptureOrder()
    {
        var capture1 = CreateCaptureTriageItem();
        var pending1 = CreatePendingItem();
        var pending2 = CreatePendingItem();

        // Set timestamps: pending is older than captures
        SetCreatedAt(pending1, DateTimeOffset.UtcNow.AddMinutes(-10));
        SetCreatedAt(pending2, DateTimeOffset.UtcNow.AddMinutes(-9));
        SetCreatedAt(capture1, DateTimeOffset.UtcNow.AddMinutes(-5));

        var captures = new List<LlmRequest> { capture1 };
        var pending = new List<LlmRequest> { pending1, pending2 };

        var batch = InvokeBuildFairBatchItems(captures, pending, 10);

        batch.Should().HaveCount(3);
        // Pending is older, so takeCaptureFirst=false.
        // Interleaving order: pending, capture, pending
        GetIsCaptureTriage(batch[0]).Should().BeFalse("first item should be pending (older)");
        GetIsCaptureTriage(batch[1]).Should().BeTrue("second item should be capture (interleaved)");
        GetIsCaptureTriage(batch[2]).Should().BeFalse("third item should be pending");
    }

    private static IList<object> InvokeBuildFairBatchItems(
        IReadOnlyList<LlmRequest> captures,
        IReadOnlyList<LlmRequest> pending,
        int maxBatchSize)
    {
        var method = typeof(LlmQueueToProposalWorker).GetMethod(
            "BuildFairBatchItems",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull("BuildFairBatchItems must exist");
        var result = method!.Invoke(null, [captures, pending, maxBatchSize]);
        result.Should().NotBeNull();
        // The result is a List<WorkerBatchItem> (private record struct) — use dynamic
        var list = (System.Collections.IList)result!;
        return list.Cast<object>().ToList();
    }

    private static void SetCreatedAt(LlmRequest item, DateTimeOffset createdAt)
    {
        // CreatedAt has a protected setter; use reflection to set it for test control
        var prop = typeof(Entity).GetProperty("CreatedAt");
        prop.Should().NotBeNull("CreatedAt property must exist on Entity");
        prop!.GetSetMethod(nonPublic: true)!.Invoke(item, [createdAt]);
    }

    private static bool GetIsCaptureTriage(object batchItem)
    {
        var prop = batchItem.GetType().GetProperty("IsCaptureTriage");
        prop.Should().NotBeNull();
        return (bool)prop!.GetValue(batchItem)!;
    }

    #endregion

    #region Concurrency: already-claimed item

    [Fact]
    public async Task ProcessBatch_ItemClaimedBetweenFetchAndProcess_SkipsGracefully()
    {
        // Simulate a race: item is Pending when the batch is built,
        // but another worker claims it (transitions to Processing)
        // before ProcessSingleItemAsync re-fetches and tries MarkAsProcessing.
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item])
        {
            // Hook: when GetByIdAsync is called, transition the item to Processing
            // before returning, simulating another worker claiming it first.
            OnBeforeGetById = i => { if (i.Status == RequestStatus.Pending) i.MarkAsProcessing(); }
        };
        var planner = new FakeAutomationPlannerService();
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        // Should not throw - the worker catches DomainException from double MarkAsProcessing
        var act = async () => await InvokeProcessBatchAsync(worker, CancellationToken.None);
        await act.Should().NotThrowAsync();

        planner.CallCount.Should().Be(0, "already-claimed items should not be processed");
    }

    #endregion

    #region Capture triage: claim fails

    [Fact]
    public async Task ProcessBatch_CaptureClaimFails_SkipsItem()
    {
        var item = CreateCaptureTriageItem();
        var queueRepo = new FakeLlmQueueRepository([], [item])
        {
            TryClaimProcessingCaptureResult = false
        };
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService: triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        triageService.CallCount.Should().Be(0, "claim-failed items should not be triaged");
    }

    #endregion

    #region Retry backoff

    [Fact]
    public void GetRetryBackoffSeconds_EmptyArray_ReturnsZero()
    {
        var settings = DefaultSettings(retryBackoff: []);
        var (worker, sp) = CreateWorkerForBackoffTest(settings);
        using (sp)
        {
            var result = InvokeGetRetryBackoffSeconds(worker, 0);
            result.Should().Be(0);
        }
    }

    [Fact]
    public void GetRetryBackoffSeconds_SingleElement_AlwaysReturnsThatElement()
    {
        var settings = DefaultSettings(retryBackoff: [42]);
        var (worker, sp) = CreateWorkerForBackoffTest(settings);
        using (sp)
        {
            InvokeGetRetryBackoffSeconds(worker, 0).Should().Be(42);
            InvokeGetRetryBackoffSeconds(worker, 1).Should().Be(42);
            InvokeGetRetryBackoffSeconds(worker, 5).Should().Be(42);
        }
    }

    [Fact]
    public void GetRetryBackoffSeconds_OutOfRange_ClampsToLastElement()
    {
        var settings = DefaultSettings(retryBackoff: [1, 5, 30]);
        var (worker, sp) = CreateWorkerForBackoffTest(settings);
        using (sp)
        {
            InvokeGetRetryBackoffSeconds(worker, 10).Should().Be(30);
        }
    }

    private static (LlmQueueToProposalWorker Worker, ServiceProvider Provider) CreateWorkerForBackoffTest(WorkerSettings settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork(new FakeLlmQueueRepository([])));
        services.AddSingleton<IAutomationPlannerService>(new FakeAutomationPlannerService());
        services.AddSingleton<ICaptureTriageService>(new FakeCaptureTriageService());
        var sp = services.BuildServiceProvider();
        var worker = new LlmQueueToProposalWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            settings,
            new WorkerHeartbeatRegistry(),
            NullLogger<LlmQueueToProposalWorker>.Instance);
        return (worker, sp);
    }

    private static int InvokeGetRetryBackoffSeconds(LlmQueueToProposalWorker worker, int retryCount)
    {
        var method = typeof(LlmQueueToProposalWorker).GetMethod(
            "GetRetryBackoffSeconds",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (int)method!.Invoke(worker, [retryCount])!;
    }

    #endregion

    #region Multiple pending items processed in batch

    [Fact]
    public async Task ProcessBatch_MultiplePendingItems_ProcessesAll()
    {
        var items = Enumerable.Range(0, 3).Select(_ => CreatePendingItem()).ToList();
        var queueRepo = new FakeLlmQueueRepository(items);
        var planner = new FakeAutomationPlannerService();
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        items.Should().OnlyContain(i => i.Status == RequestStatus.Completed);
        planner.CallCount.Should().Be(3);
    }

    #endregion

    #region Capture triage: unhandled exception

    [Fact]
    public async Task ProcessBatch_CaptureTriageThrows_HandlesWithRetry()
    {
        var item = CreateCaptureTriageItem();
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        var triageService = new FakeCaptureTriageService
        {
            ResultFactory = (_, _, _, _, _) => throw new InvalidOperationException("Triage crash")
        };
        using var sp = BuildServiceProvider(queueRepo, triageService: triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // retryAsProcessing: true, so item goes to Processing after retry reset
        item.Status.Should().Be(RequestStatus.Processing);
        item.RetryCount.Should().Be(1);
    }

    #endregion

    #region Fakes

    private sealed class FakeLlmQueueRepository : ILlmQueueRepository
    {
        private readonly List<LlmRequest> _allItems;
        public bool TryClaimProcessingCaptureResult { get; set; } = true;
        public Action<LlmRequest>? OnBeforeGetById { get; set; }

        public FakeLlmQueueRepository(
            IEnumerable<LlmRequest> pendingItems,
            IEnumerable<LlmRequest>? processingCaptureItems = null)
        {
            _allItems = pendingItems.ToList();
            if (processingCaptureItems != null)
            {
                _allItems.AddRange(processingCaptureItems);
            }
        }

        public Task<IEnumerable<LlmRequest>> GetByStatusAsync(
            RequestStatus status,
            CancellationToken cancellationToken = default)
        {
            var result = _allItems.Where(i => i.Status == status).ToList();
            return Task.FromResult<IEnumerable<LlmRequest>>(result);
        }

        public Task<LlmRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var item = _allItems.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                OnBeforeGetById?.Invoke(item);
            }
            return Task.FromResult(item);
        }

        public Task<bool> TryClaimProcessingCaptureAsync(
            Guid requestId,
            DateTimeOffset expectedUpdatedAt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TryClaimProcessingCaptureResult);
        }

        // Unused members below
        public Task<(int TotalCaptures, int NewCount, int FailedCount, int TriagingCount, int TriagedCount)> GetCaptureSummaryByUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult((0, 0, 0, 0, 0));

        public Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems.Where(i => i.Status == RequestStatus.Pending).Take(limit).ToList());

        public Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>([]);

        public Task<IEnumerable<LlmRequest>> GetByUserAndStatusAsync(Guid userId, RequestStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>([]);

        public Task<Dictionary<RequestStatus, int>> GetStatusCountsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new Dictionary<RequestStatus, int>());

        public Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_allItems.Where(i => i.Status == RequestStatus.Pending).FirstOrDefault());

        public Task<IEnumerable<LlmRequest>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems.ToList());

        public Task<LlmRequest> AddAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
            {
                return Task.FromResult(ResultFactory(instruction));
            }

            var dto = new ProposalDto(
                Guid.NewGuid(),
                sourceType,
                sourceReferenceId,
                boardId,
                userId,
                ProposalStatus.PendingReview,
                RiskLevel.Low,
                "Test proposal",
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTime.UtcNow.AddDays(1),
                null,
                null,
                null,
                null,
                correlationId ?? "",
                []);
            return Task.FromResult(Result.Success(dto));
        }

        public Task<Result<ProposalDto>> ParseBatchInstructionAsync(
            IReadOnlyList<string> instructions,
            Guid userId,
            Guid? boardId = null,
            CancellationToken cancellationToken = default,
            ProposalSourceType sourceType = ProposalSourceType.Manual,
            string? sourceReferenceId = null,
            string? correlationId = null)
            => throw new NotSupportedException();
    }

    private sealed class FakeCaptureTriageService : ICaptureTriageService
    {
        public int CallCount { get; private set; }

        public Func<Guid, Guid, Guid?, CapturePayloadV1, CancellationToken, Result<CaptureTriageProposalResultDto>>? ResultFactory { get; set; }

        public Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromCaptureAsync(
            Guid captureItemId,
            Guid userId,
            Guid? boardId,
            CapturePayloadV1 payload,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ResultFactory != null)
            {
                return Task.FromResult(ResultFactory(captureItemId, userId, boardId, payload, cancellationToken));
            }

            var result = new CaptureTriageProposalResultDto(
                captureItemId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                "v1",
                "mock",
                "mock-model");
            return Task.FromResult(Result.Success(result));
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public FakeUnitOfWork(ILlmQueueRepository llmQueue)
        {
            LlmQueue = llmQueue;
        }

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
        public IOAuthAuthCodeRepository OAuthAuthCodes => null!;
        public IApiKeyRepository ApiKeys => null!;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}
