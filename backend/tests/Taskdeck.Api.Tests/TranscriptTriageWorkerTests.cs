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

public class TranscriptTriageWorkerTests
{
    #region Helper factories

    private static LlmRequest CreateTranscriptTriageItem(
        Guid? userId = null,
        Guid? boardId = null,
        string? payload = null,
        Guid? existingProposalId = null)
    {
        var transcriptPayload = payload ?? BuildTranscriptPayloadJson(existingProposalId: existingProposalId);
        var item = new LlmRequest(
            userId ?? Guid.NewGuid(),
            CaptureRequestContract.RequestTypeTranscriptV1,
            transcriptPayload,
            boardId);
        // Transcript triage items are drained from Processing status (the API's triage
        // endpoint marks them Processing before the worker re-claims them).
        item.MarkAsProcessing();
        return item;
    }

    private static LlmRequest CreateProcessingCaptureItem(Guid? userId = null, Guid? boardId = null)
    {
        var item = new LlmRequest(
            userId ?? Guid.NewGuid(),
            CaptureRequestContract.RequestTypeV1,
            "{\"version\":1,\"source\":\"typed\",\"text\":\"Buy groceries\"}",
            boardId);
        item.MarkAsProcessing();
        return item;
    }

    private static string BuildTranscriptPayloadJson(
        string text = "Team sync: Alice to send the summary; Bob owns the release checklist",
        Guid? existingProposalId = null)
    {
        var provenancePart = existingProposalId.HasValue
            ? $",\"provenance\":{{\"captureItemId\":\"{Guid.NewGuid()}\",\"proposalId\":\"{existingProposalId.Value}\"}}"
            : "";
        return $"{{\"version\":1,\"source\":\"transcriptPaste\",\"text\":\"{text}\"{provenancePart}}}";
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

    private static TranscriptTriageWorker CreateWorker(
        IServiceScopeFactory scopeFactory,
        WorkerSettings? settings = null)
    {
        return new TranscriptTriageWorker(
            scopeFactory,
            settings ?? DefaultSettings(),
            new WorkerHeartbeatRegistry(),
            NullLogger<TranscriptTriageWorker>.Instance);
    }

    private static ServiceProvider BuildServiceProvider(
        FakeLlmQueueRepository queueRepo,
        FakeCaptureTriageService? triageService = null)
    {
        var services = new ServiceCollection();
        var unitOfWork = new FakeUnitOfWork(queueRepo);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton<ICaptureTriageService>(triageService ?? new FakeCaptureTriageService());
        return services.BuildServiceProvider();
    }

    private static async Task InvokeProcessBatchAsync(
        TranscriptTriageWorker worker,
        CancellationToken ct)
    {
        var method = typeof(TranscriptTriageWorker).GetMethod(
            "ProcessBatchAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("ProcessBatchAsync must exist");
        var task = method!.Invoke(worker, [ct]);
        task.Should().NotBeNull();
        await (Task)task!;
    }

    private static async Task InvokeExecuteAsync(
        TranscriptTriageWorker worker,
        CancellationToken ct)
    {
        var method = typeof(TranscriptTriageWorker).GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull("ExecuteAsync must exist");
        var task = method!.Invoke(worker, [ct]);
        task.Should().NotBeNull();
        await (Task)task!;
    }

    #endregion

    #region Happy path: transcript item triaged to completion

    [Fact]
    public async Task ProcessBatch_ProcessingTranscriptItem_SuccessfulTriage_MarksCompleted()
    {
        var boardId = Guid.NewGuid();
        var item = CreateTranscriptTriageItem(boardId: boardId);
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed);
        triageService.CallCount.Should().Be(1);
        triageService.LastCaptureItemId.Should().Be(item.Id);
        triageService.LastUserId.Should().Be(item.UserId);
        triageService.LastBoardId.Should().Be(boardId);
    }

    [Fact]
    public async Task ProcessBatch_TriagedWithoutProposal_CompletesWithoutStampingProposalId()
    {
        // The "triaged, nothing to propose" verdict (null ProposalId): the item completes without
        // a linked proposal — rendered as the terminal Triaged capture status, never Failed — and
        // the payload stamp carries the LLM's identity but no proposalId.
        var item = CreateTranscriptTriageItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService
        {
            ResultFactory = (captureItemId, _, _, _, _) => Result.Success(new CaptureTriageProposalResultDto(
                captureItemId,
                Guid.NewGuid(),
                ProposalId: null,
                OperationCount: 0,
                "llm-triage.v1",
                "OpenAI",
                "gpt-4o-mini"))
        };
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed);
        item.Payload.Should().Contain("\"proposalId\":null");
        item.Payload.Should().Contain("\"provider\":\"OpenAI\"");
        item.Payload.Should().Contain("\"model\":\"gpt-4o-mini\"");
        item.Payload.Should().Contain("\"promptVersion\":\"llm-triage.v1\"");
        CaptureStatusPolicy.MapFromQueueStatus(item.Status, hasLinkedProposal: false)
            .Should().Be(CaptureStatus.Triaged);
    }

    [Fact]
    public async Task ProcessBatch_SuccessfulTriage_StampsProviderModelAndProposalIntoPayload()
    {
        var item = CreateTranscriptTriageItem();
        var proposalId = Guid.NewGuid();
        var triageRunId = Guid.NewGuid();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService
        {
            ResultFactory = (captureItemId, _, _, _, _) => Result.Success(new CaptureTriageProposalResultDto(
                captureItemId,
                triageRunId,
                proposalId,
                2,
                "v2",
                "OpenAI",
                "gpt-4o-mini"))
        };
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed);
        // The persisted payload must carry the provenance stamp from the triage result
        // (camelCase JSON via CaptureRequestContract.SerializePayload).
        item.Payload.Should().Contain($"\"captureItemId\":\"{item.Id}\"");
        item.Payload.Should().Contain($"\"triageRunId\":\"{triageRunId}\"");
        item.Payload.Should().Contain($"\"proposalId\":\"{proposalId}\"");
        item.Payload.Should().Contain("\"promptVersion\":\"v2\"");
        item.Payload.Should().Contain("\"provider\":\"OpenAI\"");
        item.Payload.Should().Contain("\"model\":\"gpt-4o-mini\"");
    }

    [Fact]
    public async Task ProcessBatch_TranscriptItem_ForwardsFetchedUpdatedAtToTranscriptClaim()
    {
        var item = CreateTranscriptTriageItem();
        // Capture the value the worker should forward BEFORE running the batch:
        // successful processing mutates UpdatedAt via UpdatePayload/MarkAsCompleted.
        var expectedUpdatedAt = item.UpdatedAt;
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // Regression guard: forwarding default/now instead of the fetched UpdatedAt would
        // make the optimistic-concurrency UPDATE match nothing in production and stall the
        // transcript lane while this fake still claimed successfully.
        queueRepo.TryClaimProcessingTranscriptCalls.Should().ContainSingle();
        queueRepo.TryClaimProcessingTranscriptCalls[0].RequestId.Should().Be(item.Id);
        queueRepo.TryClaimProcessingTranscriptCalls[0].ExpectedUpdatedAt.Should().Be(expectedUpdatedAt);
    }

    #endregion

    #region Empty queue

    [Fact]
    public async Task ProcessBatch_EmptyQueue_DoesNothing()
    {
        var queueRepo = new FakeLlmQueueRepository([]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        triageService.CallCount.Should().Be(0);
        queueRepo.TryClaimProcessingTranscriptCalls.Should().BeEmpty();
    }

    #endregion

    #region Lane isolation: non-transcript items are never dispatched

    [Fact]
    public async Task ProcessBatch_NonTranscriptCaptureItem_IsNotFetchedOrTriaged()
    {
        // A Processing inbox.capture.v1 item belongs to the capture lane; the transcript
        // fetch predicate must exclude it entirely.
        var captureItem = CreateProcessingCaptureItem();
        var queueRepo = new FakeLlmQueueRepository([captureItem]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        triageService.CallCount.Should().Be(0, "capture-lane items must not reach transcript triage");
        queueRepo.TryClaimProcessingTranscriptCalls.Should().BeEmpty();
        captureItem.Status.Should().Be(RequestStatus.Processing, "the capture item is left for its own worker");
    }

    [Fact]
    public async Task ProcessBatch_RefetchReturnsNonTranscriptItem_SkipsWithoutTriage()
    {
        // Defence in depth: even if the fetch somehow surfaced a non-transcript row (or the row
        // mutated between fetch and claim), the post-claim re-fetch guard must drop it.
        var captureItem = CreateProcessingCaptureItem();
        var originalPayload = captureItem.Payload;
        var queueRepo = new FakeLlmQueueRepository([captureItem])
        {
            // Force the transcript fetch to (incorrectly) surface the capture-lane item; the
            // claim succeeds, so only the re-fetch guard stands between it and triage.
            TranscriptFetchOverride = [captureItem]
        };
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        triageService.CallCount.Should().Be(0, "the re-fetch guard must reject non-transcript request types");
        captureItem.Status.Should().Be(RequestStatus.Processing, "the guarded item is left untouched");
        captureItem.Payload.Should().Be(originalPayload);
    }

    [Fact]
    public async Task ProcessBatch_MixedLanes_OnlyTranscriptLaneQueriesUsed()
    {
        var transcriptItem = CreateTranscriptTriageItem();
        var captureItem = CreateProcessingCaptureItem();
        var queueRepo = new FakeLlmQueueRepository([transcriptItem, captureItem]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        queueRepo.GetOldestProcessingTranscriptCallCount.Should().Be(1, "the worker fetches via the transcript lane");
        queueRepo.CountProcessingTranscriptCallCount.Should().Be(1, "the backlog gauge uses the transcript count");
        queueRepo.GetOldestProcessingCaptureCallCount.Should().Be(0, "the worker must never touch the capture-lane fetch");
        queueRepo.CountProcessingCaptureCallCount.Should().Be(0, "the worker must never touch the capture-lane count");
        transcriptItem.Status.Should().Be(RequestStatus.Completed);
        captureItem.Status.Should().Be(RequestStatus.Processing, "the capture item is left for its own worker");
    }

    #endregion

    #region Concurrency: claim contention

    [Fact]
    public async Task ProcessBatch_TranscriptClaimFails_SkipsItem()
    {
        var item = CreateTranscriptTriageItem();
        var queueRepo = new FakeLlmQueueRepository([item])
        {
            // Atomic claim returns false, simulating another worker claiming it first.
            TryClaimProcessingTranscriptResult = false
        };
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        var act = async () => await InvokeProcessBatchAsync(worker, CancellationToken.None);
        await act.Should().NotThrowAsync();

        triageService.CallCount.Should().Be(0, "claim-failed items should not be triaged");
        item.Status.Should().Be(RequestStatus.Processing);
    }

    #endregion

    #region Payload provenance short-circuit

    [Fact]
    public async Task ProcessBatch_PayloadAlreadyLinkedToProposal_CompletesWithoutTriage()
    {
        var existingProposalId = Guid.NewGuid();
        var item = CreateTranscriptTriageItem(existingProposalId: existingProposalId);
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Completed);
        triageService.CallCount.Should().Be(0, "triage should be skipped for already-linked transcripts");
    }

    #endregion

    #region Triage failure: retry classification

    [Fact]
    public async Task ProcessBatch_TriageReturnsTransientError_RetriesAsProcessing()
    {
        var item = CreateTranscriptTriageItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService
        {
            ResultFactory = (_, _, _, _, _) =>
                Result.Failure<CaptureTriageProposalResultDto>(ErrorCodes.UnexpectedError, "Triage failed")
        };
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // Transcript retries are ALWAYS re-queued as Processing (never Pending): the transcript
        // lane only ever reads Processing rows, so a Pending retry would strand forever.
        item.Status.Should().Be(RequestStatus.Processing);
        item.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_TriageReturnsNonTransientError_FailsPermanently()
    {
        var item = CreateTranscriptTriageItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService
        {
            ResultFactory = (_, _, _, _, _) =>
                Result.Failure<CaptureTriageProposalResultDto>(ErrorCodes.ValidationError, "Bad input")
        };
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Failed);
        item.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_TriageReturnsTransientError_AtMaxRetries_FailsPermanently()
    {
        var item = CreateTranscriptTriageItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService
        {
            ResultFactory = (_, _, _, _, _) =>
                Result.Failure<CaptureTriageProposalResultDto>(ErrorCodes.UnexpectedError, "Transient failure")
        };
        using var sp = BuildServiceProvider(queueRepo, triageService);
        // MaxRetries=1 means after the first failure (RetryCount becomes 1), no more retries.
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 1, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        item.Status.Should().Be(RequestStatus.Failed);
        item.RetryCount.Should().Be(1);
    }

    #endregion

    #region Triage failure: unhandled exception

    [Fact]
    public async Task ProcessBatch_TriageThrowsException_RetriesAsProcessing()
    {
        var item = CreateTranscriptTriageItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService
        {
            ResultFactory = (_, _, _, _, _) => throw new InvalidOperationException("Triage crash")
        };
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // Unhandled exceptions map to UnexpectedError, which is transient -> Processing retry.
        item.Status.Should().Be(RequestStatus.Processing);
        item.RetryCount.Should().Be(1);
    }

    #endregion

    #region Invalid payload

    [Fact]
    public async Task ProcessBatch_UnsupportedPayloadVersion_FailsPermanentlyWithoutTriage()
    {
        var item = CreateTranscriptTriageItem(
            payload: "{\"version\":2,\"source\":\"transcriptPaste\",\"text\":\"stale schema\"}");
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            DefaultSettings(maxRetries: 3, retryBackoff: [0]));

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        // ParsePayload rejects the version with ValidationError, which is non-transient.
        item.Status.Should().Be(RequestStatus.Failed);
        item.RetryCount.Should().Be(1);
        triageService.CallCount.Should().Be(0, "an unparseable payload must never reach the triage service");
    }

    #endregion

    #region Multiple transcript items processed in batch

    [Fact]
    public async Task ProcessBatch_MultipleTranscriptItems_ProcessesAll()
    {
        var items = Enumerable.Range(0, 3).Select(_ => CreateTranscriptTriageItem()).ToList();
        var queueRepo = new FakeLlmQueueRepository(items);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>());

        await InvokeProcessBatchAsync(worker, CancellationToken.None);

        items.Should().OnlyContain(i => i.Status == RequestStatus.Completed);
        triageService.CallCount.Should().Be(3);
    }

    #endregion

    #region Disabled processing

    [Fact]
    public async Task ExecuteAsync_DisabledProcessing_DoesNotProcessItems()
    {
        var item = CreateTranscriptTriageItem();
        var queueRepo = new FakeLlmQueueRepository([item]);
        var triageService = new FakeCaptureTriageService();
        using var sp = BuildServiceProvider(queueRepo, triageService);
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

        triageService.CallCount.Should().Be(0);
        queueRepo.GetOldestProcessingTranscriptCallCount.Should().Be(0, "a disabled worker must not even fetch");
        item.Status.Should().Be(RequestStatus.Processing);
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

    #region Retry backoff

    [Fact]
    public void GetRetryBackoffSeconds_EmptyArray_ReturnsZero()
    {
        var (worker, sp) = CreateWorkerForBackoffTest(DefaultSettings(retryBackoff: []));
        using (sp)
        {
            InvokeGetRetryBackoffSeconds(worker, 0).Should().Be(0);
        }
    }

    [Fact]
    public void GetRetryBackoffSeconds_OutOfRange_ClampsToLastElement()
    {
        var (worker, sp) = CreateWorkerForBackoffTest(DefaultSettings(retryBackoff: [1, 5, 30]));
        using (sp)
        {
            InvokeGetRetryBackoffSeconds(worker, 10).Should().Be(30);
        }
    }

    private static (TranscriptTriageWorker Worker, ServiceProvider Provider) CreateWorkerForBackoffTest(WorkerSettings settings)
    {
        var sp = BuildServiceProvider(new FakeLlmQueueRepository([]));
        var worker = CreateWorker(sp.GetRequiredService<IServiceScopeFactory>(), settings);
        return (worker, sp);
    }

    private static int InvokeGetRetryBackoffSeconds(TranscriptTriageWorker worker, int retryCount)
    {
        var method = typeof(TranscriptTriageWorker).GetMethod(
            "GetRetryBackoffSeconds",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (int)method!.Invoke(worker, [retryCount])!;
    }

    #endregion

    #region Fakes

    private sealed class FakeLlmQueueRepository : ILlmQueueRepository
    {
        private readonly List<LlmRequest> _allItems;

        public FakeLlmQueueRepository(IEnumerable<LlmRequest> items)
        {
            _allItems = items.ToList();
        }

        public bool TryClaimProcessingTranscriptResult { get; set; } = true;

        /// <summary>
        /// When set, GetOldestProcessingTranscriptAsync returns these items regardless of the
        /// normal lane predicate. Used to exercise the worker's post-claim re-fetch guard with
        /// an item the transcript fetch should never legitimately surface.
        /// </summary>
        public IEnumerable<LlmRequest>? TranscriptFetchOverride { get; set; }

        /// <summary>
        /// Records the (requestId, expectedUpdatedAt) the worker passed on each transcript claim
        /// attempt so tests can assert the worker forwarded the fetched item's actual UpdatedAt
        /// (not default/now). A wrong value would make the optimistic-concurrency UPDATE match
        /// nothing in production and stall the transcript lane while tests stayed green.
        /// </summary>
        public List<(Guid RequestId, DateTimeOffset ExpectedUpdatedAt)> TryClaimProcessingTranscriptCalls { get; } = [];

        public int GetOldestProcessingTranscriptCallCount { get; private set; }
        public int CountProcessingTranscriptCallCount { get; private set; }
        public int GetOldestProcessingCaptureCallCount { get; private set; }
        public int CountProcessingCaptureCallCount { get; private set; }

        public Task<IEnumerable<LlmRequest>> GetOldestProcessingTranscriptAsync(int limit, CancellationToken cancellationToken = default)
        {
            GetOldestProcessingTranscriptCallCount++;
            IEnumerable<LlmRequest> source = TranscriptFetchOverride ?? _allItems
                .Where(i => i.Status == RequestStatus.Processing && CaptureRequestContract.IsTranscriptRequestType(i.RequestType))
                .OrderBy(i => i.CreatedAt);
            return Task.FromResult<IEnumerable<LlmRequest>>(source.Take(limit).ToList());
        }

        public Task<int> CountProcessingTranscriptAsync(CancellationToken cancellationToken = default)
        {
            CountProcessingTranscriptCallCount++;
            return Task.FromResult(_allItems.Count(i =>
                i.Status == RequestStatus.Processing && CaptureRequestContract.IsTranscriptRequestType(i.RequestType)));
        }

        public Task<IEnumerable<LlmRequest>> GetOldestProcessingCaptureAsync(int limit, CancellationToken cancellationToken = default)
        {
            GetOldestProcessingCaptureCallCount++;
            var result = _allItems
                .Where(i => i.Status == RequestStatus.Processing
                    && CaptureRequestContract.IsCaptureRequestType(i.RequestType)
                    && !CaptureRequestContract.IsTranscriptRequestType(i.RequestType))
                .OrderBy(i => i.CreatedAt)
                .Take(limit)
                .ToList();
            return Task.FromResult<IEnumerable<LlmRequest>>(result);
        }

        public Task<int> CountProcessingCaptureAsync(CancellationToken cancellationToken = default)
        {
            CountProcessingCaptureCallCount++;
            return Task.FromResult(_allItems.Count(i => i.Status == RequestStatus.Processing
                && CaptureRequestContract.IsCaptureRequestType(i.RequestType)
                && !CaptureRequestContract.IsTranscriptRequestType(i.RequestType)));
        }

        public Task<bool> TryClaimProcessingTranscriptAsync(
            Guid requestId,
            DateTimeOffset expectedUpdatedAt,
            CancellationToken cancellationToken = default)
        {
            TryClaimProcessingTranscriptCalls.Add((requestId, expectedUpdatedAt));
            return Task.FromResult(TryClaimProcessingTranscriptResult);
        }

        public Task<LlmRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_allItems.FirstOrDefault(i => i.Id == id));

        // Members below are not used by TranscriptTriageWorker.

        public Task<IEnumerable<LlmRequest>> GetByStatusAsync(RequestStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems.Where(i => i.Status == status).ToList());

        public Task<IEnumerable<LlmRequest>> GetByStatusForDisplayAsync(RequestStatus status, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems
                .Where(i => i.Status == status)
                .OrderByDescending(i => i.CreatedAt)
                .Take(limit)
                .ToList());

        public Task<IEnumerable<LlmRequest>> GetOldestPendingNonCaptureAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems
                .Where(i => i.Status == RequestStatus.Pending && !CaptureRequestContract.IsCaptureRequestType(i.RequestType))
                .OrderBy(i => i.CreatedAt)
                .Take(limit)
                .ToList());

        public Task<int> CountPendingNonCaptureAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_allItems.Count(i => i.Status == RequestStatus.Pending && !CaptureRequestContract.IsCaptureRequestType(i.RequestType)));

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

        public Task<bool> TryClaimProcessingCaptureAsync(
            Guid requestId,
            DateTimeOffset expectedUpdatedAt,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> TryClaimProcessingAsync(
            Guid requestId,
            DateTimeOffset expectedUpdatedAt,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(int TotalCaptures, int NewCount, int FailedCount, int TriagingCount, int TriagedCount)> GetCaptureSummaryByUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult((0, 0, 0, 0, 0));

        public Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems.Where(i => i.Status == RequestStatus.Pending).Take(limit).ToList());

        public Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>([]);

        public Task<IEnumerable<LlmRequest>> GetCapturesByUserAsync(Guid userId, int limit, int offset, Guid? boardId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>([]);

        public Task<IEnumerable<LlmRequest>> GetByUserAndStatusAsync(Guid userId, RequestStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>([]);

        public Task<Dictionary<RequestStatus, int>> GetStatusCountsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new Dictionary<RequestStatus, int>());

        public Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_allItems.FirstOrDefault(i => i.Status == RequestStatus.Pending));

        public Task<IEnumerable<LlmRequest>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<LlmRequest>>(_allItems.ToList());

        public Task<LlmRequest> AddAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(LlmRequest entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeCaptureTriageService : ICaptureTriageService
    {
        public int CallCount { get; private set; }
        public Guid? LastCaptureItemId { get; private set; }
        public Guid? LastUserId { get; private set; }
        public Guid? LastBoardId { get; private set; }

        public Func<Guid, Guid, Guid?, CapturePayloadV1, CancellationToken, Result<CaptureTriageProposalResultDto>>? ResultFactory { get; set; }

        public Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromCaptureAsync(
            Guid captureItemId,
            Guid userId,
            Guid? boardId,
            CapturePayloadV1 payload,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCaptureItemId = captureItemId;
            LastUserId = userId;
            LastBoardId = boardId;
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
            => Task.FromResult(0);

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
