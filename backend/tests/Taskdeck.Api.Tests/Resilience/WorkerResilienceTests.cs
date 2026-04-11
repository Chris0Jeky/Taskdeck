using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Api.Workers;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests.Resilience;

/// <summary>
/// Tests that background workers handle exceptions, DB failures, cancellation, and
/// repeated errors without crashing or leaving items in corrupted states.
/// </summary>
public class WorkerResilienceTests
{
    // ── Worker Exception in Main Loop ──────────────────────────────────

    [Fact]
    public async Task LlmWorker_WhenProcessBatchThrows_LogsErrorAndContinuesToNextPoll()
    {
        // Arrange: set up a scope factory whose IUnitOfWork always throws.
        var callCount = 0;
        var scopeFactory = CreateScopeFactoryThatThrowsOnUnitOfWork(() =>
        {
            callCount++;
            throw new InvalidOperationException("Simulated DB blowup");
        });

        var logger = new InMemoryLogger<LlmQueueToProposalWorker>();
        var settings = new WorkerSettings
        {
            QueuePollIntervalSeconds = 1,
            EnableAutoQueueProcessing = true,
            MaxBatchSize = 5,
            MaxConcurrency = 1,
            RetryBackoffSeconds = new[] { 0 }
        };
        var heartbeat = new WorkerHeartbeatRegistry();

        var worker = new LlmQueueToProposalWorker(scopeFactory, settings, heartbeat, logger);

        using var cts = new CancellationTokenSource();

        // Act: run the worker for long enough to complete at least one iteration, then cancel.
        var runTask = worker.StartAsync(cts.Token);
        await Task.Delay(1500);
        cts.Cancel();

        try { await runTask; } catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);

        // Assert: the worker should have logged the error but NOT crashed;
        // it should have executed at least one iteration.
        callCount.Should().BeGreaterThanOrEqualTo(1,
            "worker should have attempted at least one batch despite DB throwing");

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error in LlmQueueToProposalWorker iteration"),
            "worker should log the exception and continue looping");

        // Heartbeat should still have been reported.
        heartbeat.GetLastHeartbeat(nameof(LlmQueueToProposalWorker)).Should().NotBeNull(
            "worker should report heartbeats even when processing fails");
    }

    [Fact]
    public async Task ProposalHousekeepingWorker_WhenDbThrows_LogsErrorAndContinuesPolling()
    {
        var callCount = 0;
        var scopeFactory = CreateScopeFactoryThatThrowsOnUnitOfWork(() =>
        {
            callCount++;
            throw new InvalidOperationException("Simulated housekeeping DB failure");
        });

        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var settings = new WorkerSettings();
        var heartbeat = new WorkerHeartbeatRegistry();

        var worker = new ProposalHousekeepingWorker(scopeFactory, settings, heartbeat, logger);

        using var cts = new CancellationTokenSource();
        var runTask = worker.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();

        try { await runTask; } catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);

        callCount.Should().BeGreaterThan(0);
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("Error in ProposalHousekeepingWorker iteration"));
        heartbeat.GetLastHeartbeat(nameof(ProposalHousekeepingWorker)).Should().NotBeNull();
    }

    // ── Worker Cancellation → Clean Shutdown ───────────────────────────

    [Fact]
    public async Task LlmWorker_WhenCancelled_ExitsWithoutCrashing()
    {
        // Arrange: the worker has nothing to process; we test clean cancellation.
        var mockLlmQueue = new Mock<ILlmQueueRepository>();
        mockLlmQueue
            .Setup(q => q.GetByStatusAsync(It.IsAny<RequestStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());

        var mockUnitOfWork = new Mock<IUnitOfWork>();
        mockUnitOfWork.Setup(u => u.LlmQueue).Returns(mockLlmQueue.Object);

        var scopeFactory = CreateScopeFactoryWithUnitOfWork(mockUnitOfWork.Object);
        var logger = new InMemoryLogger<LlmQueueToProposalWorker>();
        var settings = new WorkerSettings
        {
            QueuePollIntervalSeconds = 1,
            EnableAutoQueueProcessing = true,
            MaxBatchSize = 5,
            MaxConcurrency = 1,
            RetryBackoffSeconds = new[] { 0 }
        };
        var heartbeat = new WorkerHeartbeatRegistry();

        var worker = new LlmQueueToProposalWorker(scopeFactory, settings, heartbeat, logger);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        // Let it run at least one cycle.
        await Task.Delay(1500);

        // StopAsync triggers cancellation and waits for ExecuteAsync to complete.
        // This should NOT throw -- the BackgroundService infrastructure handles OperationCanceledException.
        var stopAct = () => worker.StopAsync(CancellationToken.None);
        await stopAct.Should().NotThrowAsync(
            "worker should handle cancellation cleanly without throwing");

        // Assert: startup log should be present (proving the worker actually ran).
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("LlmQueueToProposalWorker starting"),
            "worker should have logged startup before cancellation");

        // The worker should not have logged any errors during normal operation.
        logger.Entries.Should().NotContain(e =>
            e.Level == LogLevel.Error,
            "worker should not log errors during normal processing and cancellation");
    }

    [Fact]
    public async Task LlmWorker_WhenAutoQueueProcessingDisabled_SkipsProcessingButStillReportsHeartbeat()
    {
        var mockLlmQueue = new Mock<ILlmQueueRepository>();
        var processCallCount = 0;
        mockLlmQueue
            .Setup(q => q.GetByStatusAsync(It.IsAny<RequestStatus>(), It.IsAny<CancellationToken>()))
            .Callback(() => processCallCount++)
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());

        var mockUnitOfWork = new Mock<IUnitOfWork>();
        mockUnitOfWork.Setup(u => u.LlmQueue).Returns(mockLlmQueue.Object);

        var scopeFactory = CreateScopeFactoryWithUnitOfWork(mockUnitOfWork.Object);
        var logger = new InMemoryLogger<LlmQueueToProposalWorker>();
        var settings = new WorkerSettings
        {
            QueuePollIntervalSeconds = 1,
            EnableAutoQueueProcessing = false,   // Disabled
            MaxBatchSize = 5,
            MaxConcurrency = 1,
            RetryBackoffSeconds = new[] { 0 }
        };
        var heartbeat = new WorkerHeartbeatRegistry();

        var worker = new LlmQueueToProposalWorker(scopeFactory, settings, heartbeat, logger);

        using var cts = new CancellationTokenSource();
        var runTask = worker.StartAsync(cts.Token);
        await Task.Delay(1500);
        cts.Cancel();

        try { await runTask; } catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);

        // Queue should never have been queried because processing is disabled.
        processCallCount.Should().Be(0,
            "worker should skip batch processing when EnableAutoQueueProcessing=false");

        // But heartbeats should still be reported.
        heartbeat.GetLastHeartbeat(nameof(LlmQueueToProposalWorker)).Should().NotBeNull(
            "heartbeats should be reported even when processing is disabled");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an IServiceScopeFactory where resolving IUnitOfWork invokes
    /// the provided action (which is expected to throw).
    /// </summary>
    private static IServiceScopeFactory CreateScopeFactoryThatThrowsOnUnitOfWork(Action onResolve)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IUnitOfWork)))
            .Returns(() =>
            {
                onResolve();
                return null!;
            });

        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory
            .Setup(f => f.CreateScope())
            .Returns(mockScope.Object);

        return mockScopeFactory.Object;
    }

    /// <summary>
    /// Creates an IServiceScopeFactory that resolves a real IUnitOfWork mock.
    /// </summary>
    private static IServiceScopeFactory CreateScopeFactoryWithUnitOfWork(IUnitOfWork unitOfWork)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IUnitOfWork)))
            .Returns(unitOfWork);

        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory
            .Setup(f => f.CreateScope())
            .Returns(mockScope.Object);

        return mockScopeFactory.Object;
    }
}
