using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
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
        int[]? retryBackoff = null,
        int processingLeaseSeconds = 120)
    {
        return new WorkerSettings
        {
            EnableAutoQueueProcessing = enableProcessing,
            QueuePollIntervalSeconds = 1,
            MaxBatchSize = maxBatchSize,
            MaxConcurrency = maxConcurrency,
            MaxRetries = maxRetries,
            RetryBackoffSeconds = retryBackoff ?? [0],
            ProcessingLeaseSeconds = processingLeaseSeconds
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
        FakeCaptureTriageService? triageService = null,
        IAutomationProposalRepository? automationProposals = null)
    {
        var services = new ServiceCollection();
        var unitOfWork = new FakeUnitOfWork(queueRepo, automationProposals);
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

    [Fact]
    public async Task ProcessBatch_PendingItem_ForwardsPendingUpdatedAtToClaim()
    {
        var item = CreatePendingItem();
        // Capture the value the worker should forward BEFORE running the batch:
        // BuildFairBatchItems snapshots pending.UpdatedAt, and the claim mutates it.
        var expectedUpdatedAt = item.UpdatedAt;
        var queueRepo = new FakeLlmQueueRepository([item]);
        var planner = new FakeAutomationPlannerService();
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // Regression guard: if the worker passed default/now instead of the pending
        // item's actual UpdatedAt, the optimistic-concurrency UPDATE would match nothing
        // in production and stall the queue while this fake still claimed successfully.
        queueRepo.TryClaimProcessingCalls.Should().ContainSingle();
        queueRepo.TryClaimProcessingCalls[0].RequestId.Should().Be(item.Id);
        queueRepo.TryClaimProcessingCalls[0].ExpectedUpdatedAt.Should().Be(expectedUpdatedAt);
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
        // but another worker already claimed it (TryClaimProcessingAsync returns false).
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item])
        {
            // Atomic claim returns false, simulating another worker claiming it first.
            TryClaimProcessingResult = false
        };
        var planner = new FakeAutomationPlannerService();
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

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

    #region Stuck-Processing recovery (#1209)

    private static async Task InvokeRecoverStuckProcessingItemsAsync(
        LlmQueueToProposalWorker worker,
        CancellationToken ct)
    {
        var method = typeof(LlmQueueToProposalWorker).GetMethod(
            "RecoverStuckProcessingItemsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("RecoverStuckProcessingItemsAsync must exist");
        var task = method!.Invoke(worker, [ct]);
        task.Should().NotBeNull();
        await (Task)task!;
    }

    private static void BackdateUpdatedAt(LlmRequest item, DateTimeOffset updatedAt)
    {
        typeof(LlmRequest)
            .GetProperty(nameof(LlmRequest.UpdatedAt))!
            .SetValue(item, updatedAt);
    }

    /// <summary>
    /// Builds a non-capture request left in Processing (as if a worker claimed it then crashed), with
    /// <paramref name="retryCount"/> prior failures driven through the real transitions and its UpdatedAt
    /// backdated <paramref name="ageSeconds"/> into the past so it is older than the recovery lease.
    /// </summary>
    private static LlmRequest CreateStuckProcessingNonCaptureItem(int retryCount = 0, int ageSeconds = 10_000)
    {
        var item = CreatePendingItem();
        for (var i = 0; i < retryCount; i++)
        {
            item.MarkAsProcessing();
            item.MarkAsFailed("prior failure");
            item.ResetForRetry();
        }
        item.MarkAsProcessing();
        BackdateUpdatedAt(item, DateTimeOffset.UtcNow.AddSeconds(-ageSeconds));
        return item;
    }

    [Fact]
    public async Task RecoverStuck_NonCaptureStuckInProcessing_WithRetryBudget_ReturnsToPendingAndConsumesBudget()
    {
        var item = CreateStuckProcessingNonCaptureItem(retryCount: 0);
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), DefaultSettings(maxRetries: 3));

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Pending, "a stuck item with retry budget remaining is re-enqueued");
        item.RetryCount.Should().Be(1, "recovery counts against the retry budget so a repeatedly-crashing item cannot loop forever");
    }

    [Fact]
    public async Task RecoverStuck_NonCaptureStuckInProcessing_BudgetExhausted_MarkedFailed()
    {
        // MaxRetries=3, RetryCount=2 -> 2 + 1 < 3 is false -> no budget left -> permanent failure.
        var item = CreateStuckProcessingNonCaptureItem(retryCount: 2);
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), DefaultSettings(maxRetries: 3));

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Failed, "a stuck item with no retry budget left fails permanently");
        item.RetryCount.Should().Be(3);
        item.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RecoverStuck_FreshProcessingItem_NotSwept()
    {
        // A non-capture item just claimed (UpdatedAt = now) is still legitimately in flight and must not
        // be reclaimed mid-processing.
        var item = CreatePendingItem();
        item.MarkAsProcessing();
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), DefaultSettings(maxRetries: 3));

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Processing, "a fresh Processing item is within its lease and must be left alone");
        item.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RecoverStuck_CaptureItemStuckInProcessing_NotSwept()
    {
        // Capture-triage items self-heal via the Processing re-claim path, so the non-capture recovery
        // sweep must never touch them.
        var captureItem = CreateCaptureTriageItem();
        BackdateUpdatedAt(captureItem, DateTimeOffset.UtcNow.AddSeconds(-10_000));
        var queueRepo = new FakeLlmQueueRepository([], [captureItem]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), DefaultSettings(maxRetries: 3));

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);

        captureItem.Status.Should().Be(RequestStatus.Processing, "capture items are excluded from the non-capture recovery sweep");
    }

    [Theory]
    // ProcessingLeaseSeconds is floored at 30s: a 10s lease still protects an item only 20s old, while a
    // 40s-old item exceeds the floor and is swept -- proving the floor branch (#1209 AC4).
    [InlineData(10, 20, false)]
    [InlineData(10, 40, true)]
    // A larger lease genuinely raises the threshold: a 150s-old item (which the default 120s lease WOULD
    // sweep) is protected at 300s, while a 400s-old item is swept -- proving the threshold tracks the setting.
    [InlineData(300, 150, false)]
    [InlineData(300, 400, true)]
    public async Task RecoverStuck_HonorsProcessingLeaseThresholdAndFloor(int processingLeaseSeconds, int itemAgeSeconds, bool expectedSwept)
    {
        var item = CreateStuckProcessingNonCaptureItem(retryCount: 0, ageSeconds: itemAgeSeconds);
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, processingLeaseSeconds: processingLeaseSeconds));

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);

        item.Status.Should().Be(
            expectedSwept ? RequestStatus.Pending : RequestStatus.Processing,
            "the effective lease is Math.Max(30, ProcessingLeaseSeconds={0}) and the item is {1}s old",
            processingLeaseSeconds,
            itemAgeSeconds);
    }

    [Fact]
    public async Task RecoverStuck_RunTwice_DoesNotDoubleConsumeBudget()
    {
        // A second sweep must be idempotent: after the first sweep the item is Pending (not Processing), so
        // it is excluded from the next sweep and its retry budget is not consumed twice.
        var item = CreateStuckProcessingNonCaptureItem(retryCount: 0);
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), DefaultSettings(maxRetries: 3));

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);
        item.RetryCount.Should().Be(1);

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Pending);
        item.RetryCount.Should().Be(1, "the already-requeued item is no longer in Processing, so the second sweep is a no-op");
    }

    [Fact]
    public async Task RecoverStuck_MixedCaptureAndNonCapture_OnlyNonCaptureSwept()
    {
        var nonCapture = CreateStuckProcessingNonCaptureItem(retryCount: 0);
        var capture = CreateCaptureTriageItem();
        BackdateUpdatedAt(capture, DateTimeOffset.UtcNow.AddSeconds(-10_000));
        var queueRepo = new FakeLlmQueueRepository([], [nonCapture, capture]);
        using var sp = BuildServiceProvider(queueRepo);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), DefaultSettings(maxRetries: 3));

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);

        nonCapture.Status.Should().Be(RequestStatus.Pending, "the non-capture stuck item is recovered");
        capture.Status.Should().Be(RequestStatus.Processing, "the stuck capture item is excluded from the non-capture sweep");
    }

    [Fact]
    public async Task ProcessBatch_StuckNonCaptureItem_RecoveredAndReprocessedToCompletion()
    {
        // End-to-end (#1209 acceptance): a non-capture item abandoned in Processing is recovered to Pending
        // and then drained by the same tick, proving the item is eventually reclaimed rather than lost.
        var item = CreateStuckProcessingNonCaptureItem(retryCount: 0);
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        var planner = new FakeAutomationPlannerService();
        using var sp = BuildServiceProvider(queueRepo, planner);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed, "the recovered item is re-enqueued and processed to completion within the tick");
    }

    [Fact]
    public async Task ProcessSingleItem_WithExistingQueueProposal_CompletesWithoutReprocessing()
    {
        // #1209 review (Codex): a prior attempt created + committed the proposal then crashed before the
        // request was marked completed. Reprocessing would create a DUPLICATE PendingReview proposal, so the
        // worker must complete the request without calling the planner when a Queue-sourced proposal already
        // exists for it.
        var item = CreatePendingItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var existing = new AutomationProposal(
            ProposalSourceType.Queue,
            item.UserId,
            "Existing proposal from the crashed attempt",
            RiskLevel.Low,
            item.Id.ToString(),
            boardId: null,
            sourceReferenceId: item.Id.ToString());
        var proposals = new Mock<IAutomationProposalRepository>();
        proposals
            .Setup(p => p.GetBySourceReferenceAsync(ProposalSourceType.Queue, item.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        // The planner would FAIL if called -> proves the guard short-circuits before reprocessing.
        var planner = new FakeAutomationPlannerService
        {
            ResultFactory = _ => Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, "planner must not be called")
        };
        using var sp = BuildServiceProvider(queueRepo, planner, automationProposals: proposals.Object);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed, "the request already produced its proposal, so it is completed not reprocessed");
        planner.CallCount.Should().Be(0, "the existing-proposal guard must short-circuit before the planner");
    }

    [Theory]
    [InlineData(0)] // retry budget remains
    [InlineData(2)] // retry budget exhausted (MaxRetries=3): the Codex-flagged case -- must complete, not fail
    public async Task RecoverStuck_ProposalAlreadyExists_CompletesRegardlessOfBudget(int retryCount)
    {
        // #1209 review round 2 (Codex): if the crashed attempt already committed the proposal, the request
        // succeeded; recovery must complete it even on its last attempt, rather than marking it Failed (which
        // would mislabel a successful request and bypass the drain's completion guard).
        var item = CreateStuckProcessingNonCaptureItem(retryCount: retryCount);
        var queueRepo = new FakeLlmQueueRepository([], [item]);
        var existing = new AutomationProposal(
            ProposalSourceType.Queue,
            item.UserId,
            "Existing proposal from the crashed attempt",
            RiskLevel.Low,
            item.Id.ToString(),
            boardId: null,
            sourceReferenceId: item.Id.ToString());
        var proposals = new Mock<IAutomationProposalRepository>();
        proposals
            .Setup(p => p.GetBySourceReferenceAsync(ProposalSourceType.Queue, item.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        using var sp = BuildServiceProvider(queueRepo, automationProposals: proposals.Object);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), DefaultSettings(maxRetries: 3));

        await InvokeRecoverStuckProcessingItemsAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed, "an already-created proposal means the request succeeded; recovery completes it regardless of retry budget");
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

        public Task<IEnumerable<LlmRequest>> GetByStatusForDisplayAsync(RequestStatus status, int limit, CancellationToken cancellationToken = default)
        {
            var result = _allItems.Where(i => i.Status == status).OrderByDescending(i => i.CreatedAt).Take(limit).ToList();
            return Task.FromResult<IEnumerable<LlmRequest>>(result);
        }

        public Task<IEnumerable<LlmRequest>> GetOldestPendingNonCaptureAsync(int limit, CancellationToken cancellationToken = default)
        {
            var result = _allItems
                .Where(i => i.Status == RequestStatus.Pending && !CaptureRequestContract.IsCaptureRequestType(i.RequestType))
                .OrderBy(i => i.CreatedAt)
                .Take(limit)
                .ToList();
            return Task.FromResult<IEnumerable<LlmRequest>>(result);
        }

        public Task<IEnumerable<LlmRequest>> GetOldestProcessingCaptureAsync(int limit, CancellationToken cancellationToken = default)
        {
            var result = _allItems
                .Where(i => i.Status == RequestStatus.Processing && CaptureRequestContract.IsCaptureRequestType(i.RequestType))
                .OrderBy(i => i.CreatedAt)
                .Take(limit)
                .ToList();
            return Task.FromResult<IEnumerable<LlmRequest>>(result);
        }

        public Task<int> CountPendingNonCaptureAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_allItems.Count(i => i.Status == RequestStatus.Pending && !CaptureRequestContract.IsCaptureRequestType(i.RequestType)));

        public Task<int> CountProcessingCaptureAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_allItems.Count(i => i.Status == RequestStatus.Processing && CaptureRequestContract.IsCaptureRequestType(i.RequestType)));

        public Task<int> CountPendingCaptureAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_allItems.Count(i => i.Status == RequestStatus.Pending && CaptureRequestContract.IsCaptureRequestType(i.RequestType)));

        public Task<IReadOnlyList<LlmRequest>> GetStuckProcessingNonCaptureAsync(DateTimeOffset staleBefore, int limit, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LlmRequest> result = _allItems
                .Where(i => i.Status == RequestStatus.Processing
                    && !CaptureRequestContract.IsCaptureRequestType(i.RequestType)
                    && i.UpdatedAt <= staleBefore)
                .OrderBy(i => i.UpdatedAt)
                .Take(limit)
                .ToList();
            return Task.FromResult(result);
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

        public bool TryClaimProcessingResult { get; set; } = true;

        /// <summary>
        /// Records the (requestId, expectedUpdatedAt) the worker passed on each non-capture
        /// claim attempt so tests can assert the worker forwarded the pending item's actual
        /// UpdatedAt (not default/now). A wrong value here would make the optimistic-concurrency
        /// UPDATE match nothing in production and stall the queue while tests stayed green.
        /// </summary>
        public List<(Guid RequestId, DateTimeOffset ExpectedUpdatedAt)> TryClaimProcessingCalls { get; } = [];

        public Task<bool> TryClaimProcessingAsync(
            Guid requestId,
            DateTimeOffset expectedUpdatedAt,
            CancellationToken cancellationToken = default)
        {
            TryClaimProcessingCalls.Add((requestId, expectedUpdatedAt));
            if (TryClaimProcessingResult)
            {
                var item = _allItems.FirstOrDefault(i => i.Id == requestId);
                if (item != null && item.Status == RequestStatus.Pending)
                {
                    item.MarkAsProcessing();
                }
            }
            return Task.FromResult(TryClaimProcessingResult);
        }

        // Unused members below
        public Task<(int TotalCaptures, int NewCount, int FailedCount, int TriagingCount, int TriagedCount)> GetCaptureSummaryByUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult((0, 0, 0, 0, 0));

        public Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems.Where(i => i.Status == RequestStatus.Pending).Take(limit).ToList());

        public Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>([]);

        public Task<IEnumerable<LlmRequest>> GetCapturesByUserAsync(Guid userId, int limit, int offset, Guid? boardId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems
                .Where(i => i.UserId == userId && CaptureRequestContract.IsCaptureRequestType(i.RequestType))
                .Where(i => !boardId.HasValue || i.BoardId == null || i.BoardId == boardId.Value)
                .OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
                .Skip(offset).Take(limit).ToList());

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
        public FakeUnitOfWork(ILlmQueueRepository llmQueue, IAutomationProposalRepository? automationProposals = null)
        {
            LlmQueue = llmQueue;
            // Default to a loose mock whose GetBySourceReferenceAsync returns null, so the worker's
            // existing-proposal idempotency guard is a no-op for the normal (first-time) drain path.
            AutomationProposals = automationProposals ?? Mock.Of<IAutomationProposalRepository>();
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
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}
