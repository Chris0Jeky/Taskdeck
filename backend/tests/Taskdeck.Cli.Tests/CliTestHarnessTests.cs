using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

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
        using var launchGate = new SemaphoreSlim(initialCount: 2, maxCount: 2);
        var harnesses = Enumerable.Range(0, 6)
            .Select(index => new CliTestHarness(
                $"cli-timeout-{index}",
                processTimeout: TimeSpan.FromSeconds(1),
                processLaunchSemaphore: launchGate))
            .ToArray();
        var migrationLocks = harnesses
            .Select(harness => new FileStream(
                $"{harness.DatabasePath}.migrate.lock",
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None))
            .ToArray();

        try
        {
            var failuresTask = Task.WhenAll(harnesses.Select(CaptureFailureAsync));

            var overlapObserved = await WaitForLiveProcessCountAsync(
                harnesses,
                requiredCount: 2,
                timeout: TimeSpan.FromSeconds(1));
            overlapObserved.Should().BeTrue("the fixed two-slot gate must exercise overlapping CLI roots");

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
            foreach (var migrationLock in migrationLocks)
            {
                await migrationLock.DisposeAsync();
            }

            foreach (var harness in harnesses)
            {
                await harness.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task RunAsync_WithSingleSlotGate_StartsNextChildOnlyAfterFirstIsReaped()
    {
        using var launchGate = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        await using var firstHarness = new CliTestHarness(
            "cli-single-slot-first",
            processTimeout: TimeSpan.FromMilliseconds(500),
            processLaunchSemaphore: launchGate);
        await using var secondHarness = new CliTestHarness(
            "cli-single-slot-second",
            processTimeout: TimeSpan.FromMilliseconds(500),
            processLaunchSemaphore: launchGate);
        await using var firstMigrationLock = CreateMigrationLock(firstHarness);
        await using var secondMigrationLock = CreateMigrationLock(secondHarness);

        var firstFailureTask = CaptureFailureAsync(firstHarness);
        (await WaitForLiveProcessCountAsync(
            new[] { firstHarness },
            requiredCount: 1,
            timeout: TimeSpan.FromSeconds(1))).Should().BeTrue();

        var secondFailureTask = CaptureFailureAsync(secondHarness);
        await Task.Delay(100);
        secondHarness.LastStartedProcessId.Should().BeNull(
            "the single launch slot stays owned until the first root is reaped");

        (await firstFailureTask).Should().BeOfType<TimeoutException>();
        (await WaitForLiveProcessCountAsync(
            new[] { secondHarness },
            requiredCount: 1,
            timeout: TimeSpan.FromSeconds(1))).Should().BeTrue();
        (await secondFailureTask).Should().BeOfType<TimeoutException>();

        ProcessHasExited(firstHarness.LastStartedProcessId!.Value).Should().BeTrue();
        ProcessHasExited(secondHarness.LastStartedProcessId!.Value).Should().BeTrue();
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

    private static FileStream CreateMigrationLock(CliTestHarness harness) =>
        new(
            $"{harness.DatabasePath}.migrate.lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

    private static async Task<bool> WaitForLiveProcessCountAsync(
        IEnumerable<CliTestHarness> harnesses,
        int requiredCount,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var liveCount = harnesses
                .Select(harness => harness.LastStartedProcessId)
                .Where(processId => processId.HasValue)
                .Select(processId => processId!.Value)
                .Distinct()
                .Count(processId => !ProcessHasExited(processId));
            if (liveCount >= requiredCount)
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

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
