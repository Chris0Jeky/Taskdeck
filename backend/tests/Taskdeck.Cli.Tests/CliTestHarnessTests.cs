using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliProcessLifecycleCollection
{
    public const string Name = "CLI process lifecycle";
}

[Collection(CliProcessLifecycleCollection.Name)]
public sealed class CliTestHarnessTests
{
    [Fact]
    public async Task RunAsync_WhenChildExceedsDeadline_ReapsTheChildBeforeReturning()
    {
        await using var harness = new CliTestHarness(
            "cli-timeout",
            processTimeout: TimeSpan.FromMilliseconds(500));
        await using var migrationLock = new FileStream(
            $"{harness.DatabasePath}.migrate.lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        Func<Task> action = async () => await harness.RunAsync("help");

        await action.Should().ThrowAsync<TimeoutException>();

        harness.LastStartedProcessId.Should().HaveValue();
        ProcessHasExited(harness.LastStartedProcessId!.Value).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenSixChildrenReachDeadline_ReapsEveryChild()
    {
        using var launchGate = new CliProcessLaunchGate(capacity: 2);
        var processStartedSignals = Enumerable.Range(0, 6)
            .Select(_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var processCancellations = Enumerable.Range(0, 6)
            .Select(_ => new CancellationTokenSource())
            .ToArray();
        var harnesses = Enumerable.Range(0, 6)
            .Select(index => new CliTestHarness(
                $"cli-timeout-{index}",
                processLaunchGate: launchGate,
                processCancellationToken: processCancellations[index].Token,
                processStartedSignal: processStartedSignals[index]))
            .ToArray();
        var migrationLocks = harnesses
            .Select(harness => new FileStream(
                $"{harness.DatabasePath}.migrate.lock",
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None))
            .ToArray();

        Task<Exception?[]>? failuresTask = null;
        try
        {
            failuresTask = Task.WhenAll(harnesses.Select(CaptureFailureAsync));

            var overlappingProcessIds = await Task.WhenAll(
                processStartedSignals.Take(2).Select(signal =>
                    signal.Task.WaitAsync(TimeSpan.FromSeconds(10))));
            overlappingProcessIds.Should().OnlyHaveUniqueItems();
            overlappingProcessIds.Should().OnlyContain(processId => !ProcessHasExited(processId),
                "the fixed two-slot gate must exercise overlapping CLI roots");

            foreach (var cancellation in processCancellations)
            {
                cancellation.Cancel();
            }

            var failures = await failuresTask;

            failures.Should().OnlyContain(failure => failure is TimeoutException);
            foreach (var harness in harnesses)
            {
                harness.LastStartedProcessId.Should().HaveValue();
                ProcessHasExited(harness.LastStartedProcessId!.Value).Should().BeTrue();
            }
        }
        finally
        {
            foreach (var cancellation in processCancellations)
            {
                cancellation.Cancel();
            }

            if (failuresTask is not null)
            {
                await failuresTask;
            }

            foreach (var migrationLock in migrationLocks)
            {
                await migrationLock.DisposeAsync();
            }

            foreach (var harness in harnesses)
            {
                await harness.DisposeAsync();
            }

            foreach (var cancellation in processCancellations)
            {
                cancellation.Dispose();
            }
        }
    }

    [Fact]
    public async Task RunAsync_WithSingleSlotGate_StartsNextChildOnlyAfterFirstIsReaped()
    {
        using var launchGate = new CliProcessLaunchGate(capacity: 1);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var firstStartedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStartedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task ReapAfterBarrierAsync(Process process)
        {
            cleanupEntered.TrySetResult(true);
            await allowCleanup.Task;
            await ReapProcessAsync(process);
        }

        await using var firstHarness = new CliTestHarness(
            "cli-single-slot-first",
            processLaunchGate: launchGate,
            processCancellationToken: firstCancellation.Token,
            terminateAndReapAsync: ReapAfterBarrierAsync,
            processStartedSignal: firstStartedSignal);
        await using var secondHarness = new CliTestHarness(
            "cli-single-slot-second",
            processLaunchGate: launchGate,
            processCancellationToken: secondCancellation.Token,
            processStartedSignal: secondStartedSignal);
        await using var firstMigrationLock = CreateMigrationLock(firstHarness);
        await using var secondMigrationLock = CreateMigrationLock(secondHarness);

        Task<Exception?>? firstFailureTask = null;
        Task<Exception?>? secondFailureTask = null;
        try
        {
            firstFailureTask = CaptureFailureAsync(firstHarness);
            var firstProcessId = await firstStartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ProcessHasExited(firstProcessId).Should().BeFalse();

            secondFailureTask = CaptureFailureAsync(secondHarness);
            launchGate.WaitingCount.Should().Be(1,
                "the second invocation must be explicitly waiting for the occupied slot");
            secondStartedSignal.Task.IsCompleted.Should().BeFalse();
            secondHarness.LastStartedProcessId.Should().BeNull();

            firstCancellation.Cancel();
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            launchGate.WaitingCount.Should().Be(1,
                "cleanup has not reaped the first root or released its slot");
            secondStartedSignal.Task.IsCompleted.Should().BeFalse(
                "the second child cannot start while first-root cleanup is held at the barrier");
            ProcessHasExited(firstProcessId).Should().BeFalse();

            allowCleanup.TrySetResult(true);
            (await firstFailureTask).Should().BeOfType<TimeoutException>();
            ProcessHasExited(firstProcessId).Should().BeTrue();

            var secondProcessId = await secondStartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ProcessHasExited(secondProcessId).Should().BeFalse();
            secondCancellation.Cancel();
            (await secondFailureTask).Should().BeOfType<TimeoutException>();
            ProcessHasExited(secondProcessId).Should().BeTrue();
        }
        finally
        {
            allowCleanup.TrySetResult(true);
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            await SettleAsync(firstFailureTask, secondFailureTask);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhenOutputDrainFails_ImmediatelyReapsBeforeReleasingSlotAndPreservesFailure(
        bool cancellationCallbackThrows)
    {
        using var launchGate = new CliProcessLaunchGate(capacity: 1);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var firstStartedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStartedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failOutputDrain = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var drainFailure = new IOException("Synthetic standard-output drain failure.");
        var cancellationFailure = new InvalidOperationException("Synthetic cancellation callback failure.");

        async Task<string> FailOutputDrainAsync(Process unusedProcess, CancellationToken cancellationToken)
        {
            if (cancellationCallbackThrows)
            {
                _ = cancellationToken.Register(() => throw cancellationFailure);
            }

            await failOutputDrain.Task;
            throw drainFailure;
        }

        async Task ReapAfterBarrierAsync(Process process)
        {
            cleanupEntered.TrySetResult(true);
            await allowCleanup.Task;
            await ReapProcessAsync(process);
        }

        await using var firstHarness = new CliTestHarness(
            "cli-drain-failure-first",
            processLaunchGate: launchGate,
            processCancellationToken: firstCancellation.Token,
            terminateAndReapAsync: ReapAfterBarrierAsync,
            readStandardOutputAsync: FailOutputDrainAsync,
            processStartedSignal: firstStartedSignal);
        await using var secondHarness = new CliTestHarness(
            "cli-drain-failure-second",
            processLaunchGate: launchGate,
            processCancellationToken: secondCancellation.Token,
            processStartedSignal: secondStartedSignal);
        await using var firstMigrationLock = CreateMigrationLock(firstHarness);
        await using var secondMigrationLock = CreateMigrationLock(secondHarness);

        Task<Exception?>? firstFailureTask = null;
        Task<Exception?>? secondFailureTask = null;
        try
        {
            firstFailureTask = CaptureFailureAsync(firstHarness);
            var firstProcessId = await firstStartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ProcessHasExited(firstProcessId).Should().BeFalse();

            secondFailureTask = CaptureFailureAsync(secondHarness);
            launchGate.WaitingCount.Should().Be(1);
            secondStartedSignal.Task.IsCompleted.Should().BeFalse();

            failOutputDrain.TrySetResult(true);
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            launchGate.WaitingCount.Should().Be(1,
                "a post-start failure must retain its slot until cleanup proves reap");
            secondStartedSignal.Task.IsCompleted.Should().BeFalse();
            ProcessHasExited(firstProcessId).Should().BeFalse();

            allowCleanup.TrySetResult(true);
            var firstFailure = await firstFailureTask;
            if (cancellationCallbackThrows)
            {
                var aggregateFailure = firstFailure.Should().BeOfType<AggregateException>().Which;
                aggregateFailure.InnerExceptions.Should().HaveCount(2);
                aggregateFailure.InnerExceptions[0].Should().BeSameAs(drainFailure);
                aggregateFailure.InnerExceptions[1].Should().BeOfType<AggregateException>()
                    .Which.InnerExceptions.Should().ContainSingle()
                    .Which.Should().BeSameAs(cancellationFailure);
            }
            else
            {
                firstFailure.Should().BeSameAs(drainFailure,
                    "successful cleanup must preserve the original post-start failure");
            }

            ProcessHasExited(firstProcessId).Should().BeTrue();

            var secondProcessId = await secondStartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ProcessHasExited(secondProcessId).Should().BeFalse();
            secondCancellation.Cancel();
            (await secondFailureTask).Should().BeOfType<TimeoutException>();
            ProcessHasExited(secondProcessId).Should().BeTrue();
        }
        finally
        {
            failOutputDrain.TrySetResult(true);
            allowCleanup.TrySetResult(true);
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            await SettleAsync(firstFailureTask, secondFailureTask);
        }
    }

    [Fact]
    public async Task RunAsync_WhenDrainCancellationAndCleanupFail_PreservesAllCausesAndPoisonsGate()
    {
        using var launchGate = new CliProcessLaunchGate(capacity: 1);
        var firstStartedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStartedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failOutputDrain = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var drainFailure = new IOException("Synthetic standard-output drain failure.");
        var cancellationFailure = new InvalidOperationException("Synthetic cancellation callback failure.");
        var cleanupFailure = new InvalidOperationException("Synthetic cleanup failure.");

        async Task<string> FailOutputDrainAsync(Process unusedProcess, CancellationToken cancellationToken)
        {
            _ = cancellationToken.Register(() => throw cancellationFailure);
            await failOutputDrain.Task;
            throw drainFailure;
        }

        await using var firstHarness = new CliTestHarness(
            "cli-combined-failure-first",
            processLaunchGate: launchGate,
            terminateAndReapAsync: process => ReapThenThrowAsync(process, cleanupFailure),
            readStandardOutputAsync: FailOutputDrainAsync,
            processStartedSignal: firstStartedSignal);
        await using var secondHarness = new CliTestHarness(
            "cli-combined-failure-second",
            processLaunchGate: launchGate,
            processStartedSignal: secondStartedSignal);
        await using var firstMigrationLock = CreateMigrationLock(firstHarness);

        Task<Exception?>? firstFailureTask = null;
        Task<Exception?>? secondFailureTask = null;
        try
        {
            firstFailureTask = CaptureFailureAsync(firstHarness);
            var firstProcessId = await firstStartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ProcessHasExited(firstProcessId).Should().BeFalse();

            secondFailureTask = CaptureFailureAsync(secondHarness);
            launchGate.WaitingCount.Should().Be(1);
            secondStartedSignal.Task.IsCompleted.Should().BeFalse();

            failOutputDrain.TrySetResult(true);
            var failures = await Task.WhenAll(firstFailureTask, secondFailureTask);

            var aggregateFailure = failures[0].Should().BeOfType<AggregateException>().Which;
            aggregateFailure.InnerExceptions.Should().HaveCount(3);
            aggregateFailure.InnerExceptions[0].Should().BeSameAs(drainFailure);
            aggregateFailure.InnerExceptions[1].Should().BeOfType<AggregateException>()
                .Which.InnerExceptions.Should().ContainSingle()
                .Which.Should().BeSameAs(cancellationFailure);
            aggregateFailure.InnerExceptions[2].Should().BeSameAs(cleanupFailure);

            failures[1].Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain("launch gate is poisoned");
            launchGate.IsPoisoned.Should().BeTrue();
            launchGate.CurrentCount.Should().Be(0,
                "cleanup failure must retain the acquired capacity");
            secondStartedSignal.Task.IsCompleted.Should().BeFalse();
            secondHarness.LastStartedProcessId.Should().BeNull();
            ProcessHasExited(firstProcessId).Should().BeTrue();
        }
        finally
        {
            failOutputDrain.TrySetResult(true);
            await SettleAsync(firstFailureTask, secondFailureTask);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_WhenCleanupFails_PoisonsGateAndRejectsQueuedLaunch(bool timeoutFailure)
    {
        using var launchGate = new CliProcessLaunchGate(capacity: 1);
        using var firstCancellation = new CancellationTokenSource();
        var firstStartedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStartedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception cleanupFailure = timeoutFailure
            ? new TimeoutException("Synthetic cleanup timeout.")
            : new InvalidOperationException("Synthetic non-timeout cleanup failure.");
        await using var firstHarness = new CliTestHarness(
            "cli-poisoned-gate-first",
            processLaunchGate: launchGate,
            processCancellationToken: firstCancellation.Token,
            terminateAndReapAsync: process => ReapThenThrowAsync(process, cleanupFailure),
            processStartedSignal: firstStartedSignal);
        await using var secondHarness = new CliTestHarness(
            "cli-poisoned-gate-second",
            processLaunchGate: launchGate,
            processStartedSignal: secondStartedSignal);
        await using var firstMigrationLock = CreateMigrationLock(firstHarness);

        Task<Exception?>? firstFailureTask = null;
        Task<Exception?>? secondFailureTask = null;
        try
        {
            firstFailureTask = CaptureFailureAsync(firstHarness);
            var firstProcessId = await firstStartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ProcessHasExited(firstProcessId).Should().BeFalse();

            secondFailureTask = CaptureFailureAsync(secondHarness);
            launchGate.WaitingCount.Should().Be(1);
            secondStartedSignal.Task.IsCompleted.Should().BeFalse();

            firstCancellation.Cancel();
            var failures = await Task.WhenAll(firstFailureTask, secondFailureTask);

            if (timeoutFailure)
            {
                failures[0].Should().BeOfType<TimeoutException>()
                    .Which.Message.Should().Contain("cleanup could not prove");
            }
            else
            {
                var aggregateFailure = failures[0].Should().BeOfType<AggregateException>().Which;
                aggregateFailure.InnerExceptions.Should().HaveCount(2);
                aggregateFailure.InnerExceptions[0].Should().BeAssignableTo<OperationCanceledException>();
                aggregateFailure.InnerExceptions[1].Should().BeSameAs(cleanupFailure);
            }

            failures[1].Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain("launch gate is poisoned");
            launchGate.IsPoisoned.Should().BeTrue();
            launchGate.CurrentCount.Should().Be(0,
                "cleanup failure must retain the acquired capacity");
            secondStartedSignal.Task.IsCompleted.Should().BeFalse();
            secondHarness.LastStartedProcessId.Should().BeNull();
            ProcessHasExited(firstProcessId).Should().BeTrue();
        }
        finally
        {
            firstCancellation.Cancel();
            await SettleAsync(firstFailureTask, secondFailureTask);
        }
    }

    [Fact]
    public void Constructor_WhenTimeoutIsNotPositive_RejectsBeforeCreatingTemporaryDirectory()
    {
        foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1) })
        {
            var prefix = $"cli-invalid-timeout-{Guid.NewGuid():N}";

            Action action = () => _ = new CliTestHarness(prefix, processTimeout: timeout);

            action.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("processTimeout");
            Directory.EnumerateDirectories(Path.GetTempPath(), $"{prefix}-*").Should().BeEmpty();
        }
    }

    [Fact]
    public async Task TerminateAndReapAsync_WhenTrackedProcessesExitSlowly_WaitsForRootAndDescendant()
    {
        var liveProcessIds = new HashSet<int> { 101, 202 };
        var elapsed = TimeSpan.Zero;
        var delayCount = 0;

        await CliTestHarness.TerminateAndReapAsync(
            trackedProcessIds: liveProcessIds.ToArray(),
            killProcessTree: () => { },
            killRootProcess: () => throw new InvalidOperationException("Root fallback must not run."),
            isProcessRunning: liveProcessIds.Contains,
            getElapsed: () => elapsed,
            delayAsync: delay =>
            {
                delayCount++;
                elapsed += delay;
                liveProcessIds.Remove(delayCount == 1 ? 101 : 202);
                return Task.CompletedTask;
            },
            terminationTimeout: TimeSpan.FromSeconds(1),
            pollInterval: TimeSpan.FromMilliseconds(100));

        delayCount.Should().Be(2);
        liveProcessIds.Should().BeEmpty();
    }

    [Fact]
    public async Task TerminateAndReapAsync_WhenTreeKillFails_UsesRootFallbackAndWaitsForEveryTrackedPid()
    {
        var liveProcessIds = new HashSet<int> { 301, 302 };
        var elapsed = TimeSpan.Zero;
        var rootKillCount = 0;

        await CliTestHarness.TerminateAndReapAsync(
            trackedProcessIds: liveProcessIds.ToArray(),
            killProcessTree: () => throw new Win32Exception(5, "Synthetic tree-kill denial."),
            killRootProcess: () => rootKillCount++,
            isProcessRunning: liveProcessIds.Contains,
            getElapsed: () => elapsed,
            delayAsync: delay =>
            {
                elapsed += delay;
                liveProcessIds.Clear();
                return Task.CompletedTask;
            },
            terminationTimeout: TimeSpan.FromSeconds(1),
            pollInterval: TimeSpan.FromMilliseconds(100));

        rootKillCount.Should().Be(1);
        liveProcessIds.Should().BeEmpty();
    }

    [Fact]
    public async Task TerminateAndReapAsync_WhenTrackedProcessesRemain_FailsWithExactPidEvidence()
    {
        var elapsed = TimeSpan.Zero;

        Func<Task> action = () => CliTestHarness.TerminateAndReapAsync(
            trackedProcessIds: new[] { 402, 401 },
            killProcessTree: () => throw new Win32Exception(5, "Synthetic tree-kill denial."),
            killRootProcess: () => throw new InvalidOperationException("Synthetic root-kill race."),
            isProcessRunning: _ => true,
            getElapsed: () => elapsed,
            delayAsync: delay =>
            {
                elapsed += delay;
                return Task.CompletedTask;
            },
            terminationTimeout: TimeSpan.FromMilliseconds(200),
            pollInterval: TimeSpan.FromMilliseconds(100));

        var failure = await action.Should().ThrowAsync<TimeoutException>();
        failure.Which.Message.Should().Contain("401, 402");
        failure.Which.InnerException.Should().BeOfType<AggregateException>();
    }

    private static async Task<Exception?> CaptureFailureAsync(CliTestHarness harness)
    {
        try
        {
            await harness.RunAsync("help");
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task ReapThenThrowAsync(Process process, Exception failure)
    {
        await ReapProcessAsync(process);
        throw failure;
    }

    private static async Task ReapProcessAsync(Process process)
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static async Task SettleAsync(params Task<Exception?>?[] tasks)
    {
        foreach (var task in tasks.Where(task => task is not null))
        {
            await task!;
        }
    }

    private static FileStream CreateMigrationLock(CliTestHarness harness) =>
        new(
            $"{harness.DatabasePath}.migrate.lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

    private static bool ProcessHasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
