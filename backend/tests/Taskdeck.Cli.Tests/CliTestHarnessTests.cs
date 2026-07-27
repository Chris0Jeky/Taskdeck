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
        var harnesses = Enumerable.Range(0, 6)
            .Select(index => new CliTestHarness(
                $"cli-timeout-{index}",
                processTimeout: TimeSpan.FromMilliseconds(500)))
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
            var failures = await Task.WhenAll(harnesses.Select(CaptureFailureAsync));

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
