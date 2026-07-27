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
