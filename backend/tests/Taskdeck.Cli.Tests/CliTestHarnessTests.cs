using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Infrastructure.Persistence;
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
    public void DefaultProcessTimeout_LeavesBoundedCompletionBudgetAfterMigrationLockWait()
    {
        CliTestHarness.DefaultCommandCompletionBudget.Should().BePositive();
        CliTestHarness.DefaultProcessTimeout.Should().Be(
            SerializedMigrator.DefaultLockTimeout + CliTestHarness.DefaultCommandCompletionBudget);
        CliTestHarness.DefaultProcessTimeout.Should().BeGreaterThan(SerializedMigrator.DefaultLockTimeout);
    }

    [Fact]
    public async Task Constructor_DefaultDatabaseIsFullyMigratedAndEmpty()
    {
        await using var harness = new CliTestHarness("cli-template-state");

        File.Exists(harness.DatabasePath).Should().BeTrue();
        CliTestHarness.LastDatabaseTemplateDirectory.Should().NotBeNull();
        Directory.Exists(CliTestHarness.LastDatabaseTemplateDirectory!).Should().BeFalse(
            "the disposed process-owned template must not leave a persistent directory");
        using var context = CreateDatabaseContext(harness.DatabasePath);
        var migrations = context.Database.GetMigrations().ToArray();

        migrations.Should().NotBeEmpty();
        context.Database.GetAppliedMigrations().Should().Equal(migrations);
        context.Database.GetPendingMigrations().Should().BeEmpty();
        context.Boards.Should().BeEmpty();
        context.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task Constructor_ConcurrentDefaultDatabasesShareOneTemplateButNotState()
    {
        var harnesses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            Task.Run(() => new CliTestHarness($"cli-template-concurrent-{index}"))));

        try
        {
            CliTestHarness.DatabaseTemplateBuildCount.Should().Be(1);
            harnesses.Select(harness => harness.DatabasePath).Should().OnlyHaveUniqueItems();
            harnesses.Should().OnlyContain(harness => File.Exists(harness.DatabasePath));

            await using (var firstConnection = CreateSqliteConnection(harnesses[0].DatabasePath))
            {
                await firstConnection.OpenAsync();
                await using var createProbe = firstConnection.CreateCommand();
                createProbe.CommandText = "CREATE TABLE HarnessIsolationProbe (Value INTEGER NOT NULL);";
                await createProbe.ExecuteNonQueryAsync();
            }

            await using var secondConnection = CreateSqliteConnection(harnesses[1].DatabasePath);
            await secondConnection.OpenAsync();
            await using var findProbe = secondConnection.CreateCommand();
            findProbe.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'HarnessIsolationProbe';";
            Convert.ToInt64(await findProbe.ExecuteScalarAsync()).Should().Be(0);
        }
        finally
        {
            foreach (var harness in harnesses)
            {
                await harness.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task RunAsync_WhenChildExceedsDeadline_ReapsTheChildBeforeReturning()
    {
        const string sentinel = "TOP_SECRET_SENTINEL";
        await using var harness = new CliTestHarness(
            "cli-timeout",
            processTimeout: TimeSpan.FromMilliseconds(500));
        await using var migrationLock = new FileStream(
            $"{harness.DatabasePath}.migrate.lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        Func<Task> action = async () => await harness.RunAsync($"boards create {sentinel} --json");

        var timeout = await action.Should().ThrowAsync<TimeoutException>();

        harness.LastStartedProcessId.Should().HaveValue();
        ProcessHasExited(harness.LastStartedProcessId!.Value).Should().BeTrue();
        timeout.Which.Message.Should().Contain("command=boards/create")
            .And.NotContain(sentinel)
            .And.Contain("pre=process=live")
            .And.Contain("post=process=exited")
            .And.Contain("cleanup=reaped");

        // The deliberately short deadline may interrupt any startup phase on a
        // loaded Windows host. Exact trace ordering is covered separately; this
        // regression owns redaction plus the terminate-and-reap guarantee.
    }

    [Fact]
    public async Task RunAsync_WhenCanceledAfterMigrationBegins_ReapsTheChildBeforeReturning()
    {
        const string sentinel = "TOP_SECRET_SENTINEL";
        using var processCancellation = new CancellationTokenSource();
        await using var harness = new CliTestHarness(
            "cli-migration-cancellation",
            processCancellationToken: processCancellation.Token);
        await using var migrationLock = new FileStream(
            $"{harness.DatabasePath}.migrate.lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        Task<CliCommandResult>? runTask = null;
        try
        {
            runTask = harness.RunAsync($"boards create {sentinel} --json");
            await WaitForStartupPhaseAsync(
                harness.DataDirectory,
                CliStartupTrace.MigrationBeginPhase,
                TimeSpan.FromSeconds(10));

            harness.LastStartedProcessId.Should().HaveValue();
            ProcessHasExited(harness.LastStartedProcessId!.Value).Should().BeFalse();
            processCancellation.Cancel();

            Func<Task> action = async () => await runTask;
            var timeout = await action.Should().ThrowAsync<TimeoutException>();

            ProcessHasExited(harness.LastStartedProcessId.Value).Should().BeTrue();
            timeout.Which.Message.Should().Contain("command=boards/create")
                .And.NotContain(sentinel)
                .And.Contain("pre=process=live")
                .And.Contain("post=process=exited")
                .And.Contain("last=migration-begin")
                .And.Contain("cleanup=reaped");
        }
        finally
        {
            processCancellation.Cancel();
            if (runTask is not null)
            {
                try
                {
                    await runTask;
                }
                catch (Exception)
                {
                    // The assertions above own the expected failure. If phase
                    // readiness fails first, still observe the canceled run so
                    // no child or task escapes the test.
                }
            }
        }
    }

    [Theory]
    [InlineData("api-key create --name TOP_SECRET_SENTINEL", "api-key/create")]
    [InlineData("boards create TOP_SECRET_SENTINEL --json", "boards/create")]
    [InlineData("unknown TOP_SECRET_SENTINEL", "other")]
    public void DescribeCommandShape_UsesOnlyAllowlistedTokens(string arguments, string expectedShape)
    {
        var shape = CliTestHarness.DescribeCommandShape(arguments);

        shape.Should().Be(expectedShape);
        shape.Should().NotContain("TOP_SECRET_SENTINEL");
    }

    [Fact]
    public void CliTestProject_ProcessLaunchesStayBehindSharedHarness()
    {
        var sourceDirectory = GetSourceDirectory();
        var directLaunchFiles = FindProcessLaunchFiles(sourceDirectory);

        directLaunchFiles.Should().Equal(["CliTestHarness.cs"],
            "every real CLI root must share the bounded launch, timeout, and reap policy");
    }

    [Fact]
    public void ProcessLaunchInvariant_InspectsNestedSourcesAndIgnoresBuildOutput()
    {
        var sourceDirectory = Path.Combine(
            Path.GetTempPath(),
            $"taskdeck-cli-source-invariant-{Guid.NewGuid():N}");
        var nestedDirectory = Path.Combine(sourceDirectory, "nested");
        Directory.CreateDirectory(Path.Combine(nestedDirectory, "bin"));
        Directory.CreateDirectory(Path.Combine(nestedDirectory, "obj"));

        try
        {
            File.WriteAllText(
                Path.Combine(sourceDirectory, "CommentsAndStrings.cs"),
                "// new ProcessStartInfo(\"ignored\");\nvar text = \"Process.Start(\\\"ignored\\\")\";");
            File.WriteAllText(
                Path.Combine(nestedDirectory, "AlternateSyntax.cs"),
                """
                using System.Diagnostics;

                var process = new Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
                };
                """);
            File.WriteAllText(
                Path.Combine(nestedDirectory, "TargetTypedProcess.cs"),
                """
                using Process child = new()
                {
                    StartInfo = new() { FileName = "dotnet" }
                };

                child.Start();
                """);
            File.WriteAllText(
                Path.Combine(nestedDirectory, "ProcessAliases.cs"),
                """
                using P = System.Diagnostics.Process;
                using PSI = System.Diagnostics.ProcessStartInfo;

                var startInfo = new PSI();
                P.Start("dotnet");
                """);
            File.WriteAllText(
                Path.Combine(nestedDirectory, "bin", "Generated.cs"),
                "System.Diagnostics.Process.Start(\"dotnet\");");
            File.WriteAllText(
                Path.Combine(nestedDirectory, "obj", "Generated.cs"),
                "new ProcessStartInfo(\"dotnet\");");

            FindProcessLaunchFiles(sourceDirectory).Should().Equal(
                [
                    Path.Combine("nested", "AlternateSyntax.cs"),
                    Path.Combine("nested", "ProcessAliases.cs"),
                    Path.Combine("nested", "TargetTypedProcess.cs")
                ]);
        }
        finally
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_WhenCommandCompletes_RecordsFullStartupLifecycle()
    {
        await using var harness = new CliTestHarness(
            "cli-startup-trace",
            preprovisionDatabase: false);

        File.Exists(harness.DatabasePath).Should().BeFalse();

        var result = await harness.RunAsync("help");

        result.ExitCode.Should().Be(0);
        File.Exists(harness.DatabasePath).Should().BeTrue();
        var snapshot = harness.LastStartupTraceSnapshot;
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be("available");
        snapshot.RecordCount.Should().Be(8);
        snapshot.MalformedRecordCount.Should().Be(0);
        snapshot.LastPhase.Should().Be(CliStartupTrace.DisposalEndPhase);

        using var context = CreateDatabaseContext(harness.DatabasePath);
        var migrations = context.Database.GetMigrations().ToArray();
        migrations.Should().NotBeEmpty();
        context.Database.GetAppliedMigrations().Should().Equal(migrations);
        context.Database.GetPendingMigrations().Should().BeEmpty();
        context.Boards.Should().BeEmpty();
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
    public async Task RunAsync_WithDefaultGate_StartsNextChildOnlyAfterFirstIsReaped()
    {
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
            processCancellationToken: firstCancellation.Token,
            terminateAndReapAsync: ReapAfterBarrierAsync,
            processStartedSignal: firstStartedSignal);
        await using var secondHarness = new CliTestHarness(
            "cli-single-slot-second",
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
            secondStartedSignal.Task.IsCompleted.Should().BeFalse();
            secondHarness.LastStartedProcessId.Should().BeNull();

            firstCancellation.Cancel();
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
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

    private static string GetSourceDirectory([CallerFilePath] string sourceFilePath = "") =>
        Path.GetDirectoryName(sourceFilePath)
        ?? throw new InvalidOperationException("Could not resolve the CLI test source directory.");

    private static string[] FindProcessLaunchFiles(string sourceDirectory) =>
        Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(sourceDirectory, path))
            .Where(path => ContainsProcessLaunch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceDirectory, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsBuildOutputPath(string sourceDirectory, string path) =>
        Path
            .GetRelativePath(sourceDirectory, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsProcessLaunch(string source)
    {
        var tokens = TokenizeCSharp(source);
        var typeAliases = FindTypeAliases(tokens);
        var processTypeNames = GetTypeNames(typeAliases, "Process");
        var processStartInfoTypeNames = GetTypeNames(typeAliases, "ProcessStartInfo");
        var processVariables = FindTargetTypedProcessVariables(tokens, processTypeNames);

        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index] == "new"
                && TryReadQualifiedType(tokens, index + 1, out var constructedType, out _)
                && (processTypeNames.Contains(constructedType)
                    || processStartInfoTypeNames.Contains(constructedType)))
            {
                return true;
            }

            if ((processTypeNames.Contains(tokens[index]) || processVariables.Contains(tokens[index]))
                && index + 3 < tokens.Count
                && tokens[index + 1] == "."
                && tokens[index + 2] == "Start"
                && tokens[index + 3] == "(")
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, string> FindTypeAliases(IReadOnlyList<string> tokens)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index + 3 < tokens.Count; index++)
        {
            if (tokens[index] != "using"
                || !IsIdentifier(tokens[index + 1])
                || tokens[index + 2] != "=")
            {
                continue;
            }

            if (TryReadQualifiedType(tokens, index + 3, out var targetType, out var typeEnd)
                && typeEnd < tokens.Count
                && tokens[typeEnd] == ";")
            {
                aliases[tokens[index + 1]] = targetType;
                index = typeEnd;
            }
        }

        return aliases;
    }

    private static HashSet<string> GetTypeNames(
        IReadOnlyDictionary<string, string> aliases,
        string targetType)
    {
        var typeNames = new HashSet<string>(StringComparer.Ordinal) { targetType };
        foreach (var (alias, resolvedType) in aliases)
        {
            if (resolvedType == targetType)
            {
                typeNames.Add(alias);
            }
        }

        return typeNames;
    }

    private static HashSet<string> FindTargetTypedProcessVariables(
        IReadOnlyList<string> tokens,
        IReadOnlySet<string> processTypeNames)
    {
        var processVariables = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!TryReadQualifiedType(tokens, index, out var declaredType, out var typeEnd)
                || !processTypeNames.Contains(declaredType)
                || typeEnd + 3 >= tokens.Count
                || !IsIdentifier(tokens[typeEnd])
                || tokens[typeEnd + 1] != "="
                || tokens[typeEnd + 2] != "new"
                || tokens[typeEnd + 3] != "(")
            {
                continue;
            }

            processVariables.Add(tokens[typeEnd]);
            index = typeEnd;
        }

        return processVariables;
    }

    private static bool TryReadQualifiedType(
        IReadOnlyList<string> tokens,
        int startIndex,
        out string typeName,
        out int typeEnd)
    {
        typeName = string.Empty;
        typeEnd = startIndex;
        if (startIndex >= tokens.Count)
        {
            return false;
        }

        var index = startIndex;
        if (tokens[index] == "global" && index + 1 < tokens.Count && tokens[index + 1] == "::")
        {
            index += 2;
        }

        if (index >= tokens.Count || !IsIdentifier(tokens[index]))
        {
            return false;
        }

        typeName = tokens[index++];
        while (index + 1 < tokens.Count && tokens[index] == "." && IsIdentifier(tokens[index + 1]))
        {
            typeName = tokens[index + 1];
            index += 2;
        }

        typeEnd = index;
        return true;
    }

    private static bool IsIdentifier(string token) =>
        token.Length > 0 && (char.IsLetter(token[0]) || token[0] == '_');

    private static List<string> TokenizeCSharp(string source)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                index++;
                continue;
            }

            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                index = source.IndexOf('\n', index + 2);
                if (index < 0)
                {
                    break;
                }

                continue;
            }

            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = index < 0 ? source.Length : index + 2;
                continue;
            }

            if (source[index] == '"')
            {
                index = SkipQuotedLiteral(source, index);
                continue;
            }

            if (source[index] == '\'')
            {
                index = SkipCharacterLiteral(source, index);
                continue;
            }

            if (char.IsLetter(source[index]) || source[index] == '_' || source[index] == '@')
            {
                var identifierStart = index;
                if (source[index] == '@')
                {
                    index++;
                }

                while (index < source.Length
                    && (char.IsLetterOrDigit(source[index]) || source[index] == '_'))
                {
                    index++;
                }

                if (index > identifierStart + (source[identifierStart] == '@' ? 1 : 0))
                {
                    tokens.Add(source[(source[identifierStart] == '@' ? identifierStart + 1 : identifierStart)..index]);
                    continue;
                }
            }

            if (source[index] == ':' && index + 1 < source.Length && source[index + 1] == ':')
            {
                tokens.Add("::");
                index += 2;
                continue;
            }

            tokens.Add(source[index].ToString());
            index++;
        }

        return tokens;
    }

    private static int SkipQuotedLiteral(string source, int quoteIndex)
    {
        var delimiterLength = 1;
        while (quoteIndex + delimiterLength < source.Length
            && source[quoteIndex + delimiterLength] == '"')
        {
            delimiterLength++;
        }

        if (delimiterLength >= 3)
        {
            var closingDelimiter = new string('"', delimiterLength);
            var closingIndex = source.IndexOf(closingDelimiter, quoteIndex + delimiterLength, StringComparison.Ordinal);
            return closingIndex < 0 ? source.Length : closingIndex + delimiterLength;
        }

        var verbatim = quoteIndex > 0 && source[quoteIndex - 1] == '@';
        for (var index = quoteIndex + 1; index < source.Length; index++)
        {
            if (!verbatim && source[index] == '\\' && index + 1 < source.Length)
            {
                index++;
                continue;
            }

            if (source[index] != '"')
            {
                continue;
            }

            if (verbatim && index + 1 < source.Length && source[index + 1] == '"')
            {
                index++;
                continue;
            }

            return index + 1;
        }

        return source.Length;
    }

    private static int SkipCharacterLiteral(string source, int quoteIndex)
    {
        for (var index = quoteIndex + 1; index < source.Length; index++)
        {
            if (source[index] == '\\' && index + 1 < source.Length)
            {
                index++;
                continue;
            }

            if (source[index] == '\'')
            {
                return index + 1;
            }
        }

        return source.Length;
    }

    private static TaskdeckDbContext CreateDatabaseContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(CreateSqliteConnectionString(databasePath))
            .Options;
        return new TaskdeckDbContext(options);
    }

    private static SqliteConnection CreateSqliteConnection(string databasePath) =>
        new(CreateSqliteConnectionString(databasePath));

    private static string CreateSqliteConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();

    private static async Task WaitForStartupPhaseAsync(
        string dataDirectory,
        string expectedPhase,
        TimeSpan timeout)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                foreach (var tracePath in Directory.EnumerateFiles(
                    dataDirectory,
                    "startup-*.trace",
                    SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileNameWithoutExtension(tracePath);
                    var correlationId = fileName["startup-".Length..];
                    var snapshot = CliStartupTrace.ReadSnapshot(tracePath, correlationId);
                    if (string.Equals(snapshot.LastPhase, expectedPhase, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), timeoutCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"CLI startup trace did not reach the expected phase within {timeout.TotalSeconds}s.");
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
