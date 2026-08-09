using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using FluentAssertions;

namespace Taskdeck.Cli.Tests;

/// <summary>
/// Shared test harness that runs the CLI as a subprocess against an ephemeral SQLite database.
/// Each harness instance gets its own database file, so tests are isolated.
/// </summary>
internal sealed class CliTestHarness : IAsyncDisposable
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TerminationPollInterval = TimeSpan.FromMilliseconds(50);
    // Windows CI has demonstrated that overlapping real CLI roots can leave
    // unrelated launches stuck at the shared process deadline. Keep the normal
    // harness lane serial; lifecycle tests can still inject a wider gate when
    // they need to exercise concurrent cleanup behavior.
    private const int DefaultProcessLaunchLimit = 1;
    private static readonly CliProcessLaunchGate DefaultProcessLaunchGate = new(DefaultProcessLaunchLimit);

    private readonly string _dataDirectory;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly bool _provisionEncryptionKey;
    private readonly TimeSpan _processTimeout;
    private readonly CliProcessLaunchGate _processLaunchGate;
    private readonly CancellationToken _processCancellationToken;
    private readonly Func<Process, Task> _terminateAndReapAsync;
    private readonly Func<Process, CancellationToken, Task<string>> _readStandardOutputAsync;
    private readonly TaskCompletionSource<int>? _processStartedSignal;
    private int _lastStartedProcessId;
    private CliStartupTraceSnapshot? _lastStartupTraceSnapshot;

    /// <param name="provisionEncryptionKey">
    /// When true (default) the harness injects a test connector encryption key via
    /// the environment, matching a configured machine. When false the harness
    /// simulates a CLEAN machine: no key is supplied, so the CLI must bootstrap
    /// one itself.
    /// </param>
    public CliTestHarness(
        string dbPrefix = "taskdeck-cli-tests",
        bool provisionEncryptionKey = true,
        TimeSpan? processTimeout = null,
        CliProcessLaunchGate? processLaunchGate = null,
        CancellationToken processCancellationToken = default,
        Func<Process, Task>? terminateAndReapAsync = null,
        Func<Process, CancellationToken, Task<string>>? readStandardOutputAsync = null,
        TaskCompletionSource<int>? processStartedSignal = null)
    {
        _processTimeout = processTimeout ?? ProcessTimeout;
        if (_processTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTimeout));
        }

        // Each harness gets its own data directory so the SQLite file -- and any
        // appsettings.local.json written by the CLI's first-run bootstrap -- are
        // isolated from other tests and cleaned up on dispose.
        _dataDirectory = Path.Combine(Path.GetTempPath(), $"{dbPrefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDirectory);
        _databasePath = Path.Combine(_dataDirectory, "taskdeck.db");
        _connectionString = $"Data Source={_databasePath}";
        _provisionEncryptionKey = provisionEncryptionKey;
        _processLaunchGate = processLaunchGate ?? DefaultProcessLaunchGate;
        _processCancellationToken = processCancellationToken;
        _terminateAndReapAsync = terminateAndReapAsync ?? TerminateAndReapAsync;
        _readStandardOutputAsync = readStandardOutputAsync ??
            (static (process, cancellationToken) => process.StandardOutput.ReadToEndAsync(cancellationToken));
        _processStartedSignal = processStartedSignal;
    }

    /// <summary>
    /// Directory that holds this harness's SQLite database. On a fresh-machine run
    /// (<c>provisionEncryptionKey: false</c>) the CLI's first-run bootstrap writes
    /// <c>appsettings.local.json</c> here.
    /// </summary>
    public string DataDirectory => _dataDirectory;
    public string DatabasePath => _databasePath;
    internal int? LastStartedProcessId
    {
        get
        {
            var processId = Volatile.Read(ref _lastStartedProcessId);
            return processId == 0 ? null : processId;
        }
    }

    internal CliStartupTraceSnapshot? LastStartupTraceSnapshot => _lastStartupTraceSnapshot;

    public async Task<(Guid BoardId, Guid ColumnId)> CreateBoardAndColumnAsync(
        string boardName = "TestBoard",
        string columnName = "Todo")
    {
        var boardResult = await RunAsync($"boards create {boardName} --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var columnResult = await RunAsync($"columns create --board {boardId} --name {columnName} --json");
        columnResult.ExitCode.Should().Be(0, columnResult.StdErr);
        using var columnDoc = JsonDocument.Parse(columnResult.StdOut);
        var columnId = columnDoc.RootElement.GetProperty("id").GetGuid();

        return (boardId, columnId);
    }

    /// <param name="extraEnvironment">
    /// Optional environment variables applied AFTER the harness's own provisioning
    /// logic, letting a test exercise operator overrides (e.g. supplying the
    /// documented <c>TASKDECK_CONNECTORS__ENCRYPTIONKEY</c> on a clean machine).
    /// </param>
    public async Task<CliCommandResult> RunAsync(
        string arguments,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null)
    {
        await _processLaunchGate.WaitAsync();
        try
        {
            var cliDllPath = ResolveCliDllPath();
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = string.IsNullOrWhiteSpace(arguments)
                    ? $"\"{cliDllPath}\""
                    : $"\"{cliDllPath}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _dataDirectory
            };

            startInfo.Environment["TASKDECK_CONNECTION_STRING"] = _connectionString;
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            if (_provisionEncryptionKey)
            {
                // Test-only 256-bit encryption key for connector credentials.
                startInfo.Environment["Connectors__EncryptionKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            }
            else
            {
                // Clean machine: ensure no key leaks in from the parent environment so
                // the CLI's first-run bootstrap is exercised.
                startInfo.Environment.Remove("Connectors__EncryptionKey");
                startInfo.Environment.Remove("TASKDECK_CONNECTORS__ENCRYPTIONKEY");
            }

            // Test-supplied overrides win over the provisioning defaults above.
            if (extraEnvironment is not null)
            {
                foreach (var (name, value) in extraEnvironment)
                {
                    if (value is null)
                    {
                        startInfo.Environment.Remove(name);
                    }
                    else
                    {
                        startInfo.Environment[name] = value;
                    }
                }
            }

            var traceCorrelationId = Guid.NewGuid().ToString("N");
            var tracePath = CliStartupTrace.TryGetTracePath(_dataDirectory, traceCorrelationId);
            startInfo.Environment[CliStartupTrace.CorrelationEnvironmentVariable] = traceCorrelationId;

            using var process = new Process { StartInfo = startInfo };
            using var timeoutCts = new CancellationTokenSource(_processTimeout);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token,
                _processCancellationToken);

            if (!process.Start())
            {
                throw new InvalidOperationException("The CLI process could not be started.");
            }

            Task<string>? standardOutputTask = null;
            Task<string>? standardErrorTask = null;
            Task? processExitTask = null;
            try
            {
                Volatile.Write(ref _lastStartedProcessId, process.Id);
                _processStartedSignal?.TrySetResult(process.Id);

                standardOutputTask = _readStandardOutputAsync(process, cts.Token);
                standardErrorTask = process.StandardError.ReadToEndAsync(cts.Token);
                processExitTask = process.WaitForExitAsync(cts.Token);
                await ObserveProcessTasksAsync(
                    standardOutputTask,
                    standardErrorTask,
                    processExitTask);

                var result = new CliCommandResult(
                    process.ExitCode,
                    standardOutputTask.Result.Trim(),
                    standardErrorTask.Result.Trim());
                _lastStartupTraceSnapshot = CliStartupTrace.ReadSnapshot(tracePath, traceCorrelationId);
                return result;
            }
            catch (Exception executionFailure)
            {
                var commandShape = DescribeCommandShape(arguments);
                var isTimeout = executionFailure is OperationCanceledException;
                var preCancellationSnapshot = isTimeout
                    ? CaptureTimeoutSnapshot(process, standardOutputTask, standardErrorTask, processExitTask, tracePath, traceCorrelationId)
                    : null;

                // A drain failure can arrive while the child is still blocked. Cancel
                // the remaining observations immediately, then reap before retaining
                // the selected original failure. Waiting for Task.WhenAll here would
                // defer cleanup until the normal process deadline.
                Exception? observationCancellationFailure = null;
                try
                {
                    cts.Cancel();
                }
                catch (Exception exception)
                {
                    // Cancellation callbacks are allowed to throw. Preserve that
                    // evidence, but never let it bypass process cleanup.
                    observationCancellationFailure = exception;
                }

                Exception? cleanupFailure = null;
                try
                {
                    await _terminateAndReapAsync(process);
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                    _processLaunchGate.Poison(cleanupFailure);
                }

                var postCleanupSnapshot = isTimeout
                    ? CaptureTimeoutSnapshot(process, standardOutputTask, standardErrorTask, processExitTask, tracePath, traceCorrelationId)
                    : null;

                await SettleProcessTasksAsync(
                    standardOutputTask,
                    standardErrorTask,
                    processExitTask);

                if (cleanupFailure is not null)
                {
                    if (isTimeout && cleanupFailure is TimeoutException)
                    {
                        var timeoutFailures = new[]
                            {
                                executionFailure,
                                observationCancellationFailure,
                                cleanupFailure
                            }
                            .Where(failure => failure is not null)
                            .Cast<Exception>();
                        throw new TimeoutException(
                            BuildTimeoutMessage(commandShape, preCancellationSnapshot!, postCleanupSnapshot!, "failed"),
                            new AggregateException(timeoutFailures));
                    }

                    var combinedFailures = new[]
                        {
                            executionFailure,
                            observationCancellationFailure,
                            cleanupFailure
                        }
                        .Where(failure => failure is not null)
                        .Cast<Exception>();
                    throw new AggregateException(
                        $"CLI process failed after launch and cleanup also failed. Command: {commandShape}.",
                        combinedFailures);
                }

                if (isTimeout)
                {
                    throw new TimeoutException(
                        BuildTimeoutMessage(commandShape, preCancellationSnapshot!, postCleanupSnapshot!, "reaped"),
                        executionFailure);
                }

                if (observationCancellationFailure is not null)
                {
                    throw new AggregateException(
                        $"CLI process failed after launch and cancellation of remaining process " +
                        $"observations also failed. Command: {commandShape}.",
                        executionFailure,
                        observationCancellationFailure);
                }

                ExceptionDispatchInfo.Capture(executionFailure).Throw();
                throw new UnreachableException();
            }
        }
        finally
        {
            _processLaunchGate.Release();
        }
    }

    internal static string DescribeCommandShape(string arguments)
    {
        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return "other";
        }

        var group = tokens[0].ToLowerInvariant();
        var command = tokens[1].ToLowerInvariant();
        return (group, command) switch
        {
            ("boards", "create" or "list" or "delete") => $"boards/{command}",
            ("columns", "create" or "list" or "delete") => $"columns/{command}",
            ("cards", "add" or "list" or "move" or "delete") => $"cards/{command}",
            ("api-key", "create" or "list" or "revoke") => $"api-key/{command}",
            ("invites", "create" or "list" or "revoke") => $"invites/{command}",
            _ => "other"
        };
    }

    internal static string BuildTimeoutMessage(
        string commandShape,
        CliTimeoutSnapshot preCancellation,
        CliTimeoutSnapshot postCleanup,
        string cleanupOutcome) =>
        ("CLI process did not exit within timeout" +
        (cleanupOutcome == "failed" ? " and cleanup could not prove that every tracked process exited" : string.Empty) +
        $". command={commandShape}; pre={preCancellation}; post={postCleanup}; cleanup={cleanupOutcome}.");

    private static CliTimeoutSnapshot CaptureTimeoutSnapshot(
        Process process,
        Task? standardOutputTask,
        Task? standardErrorTask,
        Task? processExitTask,
        string? tracePath,
        string traceCorrelationId)
    {
        var processState = "unavailable";
        int? exitCode = null;
        try
        {
            if (process.HasExited)
            {
                processState = "exited";
                exitCode = process.ExitCode;
            }
            else
            {
                processState = "live";
            }
        }
        catch (Exception)
        {
            // A race while inspecting an owned process is diagnostic-only.
        }

        return new CliTimeoutSnapshot(
            processState,
            exitCode,
            DescribeTaskStatus(standardOutputTask),
            DescribeTaskStatus(standardErrorTask),
            DescribeTaskStatus(processExitTask),
            CliStartupTrace.ReadSnapshot(tracePath, traceCorrelationId));
    }

    private static string DescribeTaskStatus(Task? task) => task switch
    {
        null => "not-started",
        { IsCompletedSuccessfully: true } => "completed",
        { IsFaulted: true } => "faulted",
        { IsCanceled: true } => "canceled",
        _ => "pending"
    };

    private static async Task ObserveProcessTasksAsync(params Task[] orderedTasks)
    {
        var pendingTasks = orderedTasks.ToList();
        while (pendingTasks.Count > 0)
        {
            await Task.WhenAny(pendingTasks);

            // Faults take precedence over cancellation, and declaration order makes
            // the selected original exception deterministic when tasks finish together.
            var faultedTask = pendingTasks.FirstOrDefault(task => task.IsFaulted);
            if (faultedTask is not null)
            {
                await faultedTask;
            }

            var canceledTask = pendingTasks.FirstOrDefault(task => task.IsCanceled);
            if (canceledTask is not null)
            {
                await canceledTask;
            }

            pendingTasks.RemoveAll(task => task.IsCompletedSuccessfully);
        }
    }

    private static async Task SettleProcessTasksAsync(params Task?[] tasks)
    {
        foreach (var task in tasks.Where(task => task is not null))
        {
            try
            {
                await task!;
            }
            catch (Exception)
            {
                // The selected execution failure is propagated after every remaining
                // observation has reached a terminal state. Await here only observes
                // secondary faults/cancellation so they cannot escape unobserved.
            }
        }
    }

    private static async Task TerminateAndReapAsync(Process process)
    {
        // Taskdeck.Cli executes in the directly launched dotnet process and does not
        // spawn child processes. CreateNoWindow also removes the Windows conhost lane
        // seen in the failing hosted runs. Each concurrent harness root is therefore
        // tracked independently; Kill(true) remains defense-in-depth for an unexpected
        // descendant, while the direct-root fallback guarantees that a tree-kill error
        // cannot skip termination of the process this harness owns.
        var trackedProcessIds = new[] { process.Id };
        var stopwatch = Stopwatch.StartNew();

        await TerminateAndReapAsync(
            trackedProcessIds,
            killProcessTree: () => process.Kill(entireProcessTree: true),
            killRootProcess: process.Kill,
            isProcessRunning: IsProcessRunning,
            getElapsed: () => stopwatch.Elapsed,
            delayAsync: Task.Delay,
            terminationTimeout: TerminationTimeout,
            pollInterval: TerminationPollInterval);
    }

    internal static async Task TerminateAndReapAsync(
        IReadOnlyCollection<int> trackedProcessIds,
        Action killProcessTree,
        Action killRootProcess,
        Func<int, bool> isProcessRunning,
        Func<TimeSpan> getElapsed,
        Func<TimeSpan, Task> delayAsync,
        TimeSpan terminationTimeout,
        TimeSpan pollInterval)
    {
        ArgumentNullException.ThrowIfNull(trackedProcessIds);
        ArgumentNullException.ThrowIfNull(killProcessTree);
        ArgumentNullException.ThrowIfNull(killRootProcess);
        ArgumentNullException.ThrowIfNull(isProcessRunning);
        ArgumentNullException.ThrowIfNull(getElapsed);
        ArgumentNullException.ThrowIfNull(delayAsync);

        var processIds = trackedProcessIds.Distinct().Order().ToArray();
        if (processIds.Length == 0 || processIds.Any(processId => processId <= 0))
        {
            throw new ArgumentException("Tracked process IDs must contain only positive values.", nameof(trackedProcessIds));
        }

        if (terminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(terminationTimeout));
        }

        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        Exception? treeKillFailure = null;
        try
        {
            killProcessTree();
        }
        catch (Exception exception) when (IsExpectedTerminationException(exception))
        {
            treeKillFailure = exception;
        }

        Exception? rootKillFailure = null;
        if (treeKillFailure is not null)
        {
            try
            {
                killRootProcess();
            }
            catch (Exception exception) when (IsExpectedTerminationException(exception))
            {
                rootKillFailure = exception;
            }
        }

        while (true)
        {
            var liveProcessIds = processIds.Where(isProcessRunning).ToArray();
            if (liveProcessIds.Length == 0)
            {
                return;
            }

            var remaining = terminationTimeout - getElapsed();
            if (remaining <= TimeSpan.Zero)
            {
                var failures = new[] { treeKillFailure, rootKillFailure }
                    .Where(failure => failure is not null)
                    .Cast<Exception>()
                    .ToArray();
                var innerException = failures.Length switch
                {
                    0 => null,
                    1 => failures[0],
                    _ => new AggregateException(failures)
                };

                throw new TimeoutException(
                    $"Process cleanup did not reap tracked PID(s) " +
                    $"{string.Join(", ", liveProcessIds)} within {terminationTimeout.TotalSeconds}s.",
                    innerException);
            }

            await delayAsync(remaining < pollInterval ? remaining : pollInterval);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsExpectedTerminationException(Exception exception) =>
        exception is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException;

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    private static string ResolveCliDllPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Taskdeck.Cli.dll");
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException(
            $"Taskdeck.Cli.dll was not found in the test execution directory ({AppContext.BaseDirectory}). " +
            "Ensure the CLI project is referenced and built.");
    }
}

/// <summary>
/// Bounds CLI subprocess concurrency and fails closed after cleanup cannot prove
/// that a child exited. Poisoning wakes queued callers and prevents every future
/// acquisition instead of admitting more work beside an unreaped process.
/// </summary>
internal sealed class CliProcessLaunchGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly CancellationTokenSource _poisonCancellation = new();
    private Exception? _poisonReason;
    private int _waitingCount;

    public CliProcessLaunchGate(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _semaphore = new SemaphoreSlim(capacity, capacity);
    }

    internal int CurrentCount => _semaphore.CurrentCount;
    internal int WaitingCount => Volatile.Read(ref _waitingCount);
    internal bool IsPoisoned => Volatile.Read(ref _poisonReason) is not null;

    internal async Task WaitAsync()
    {
        ThrowIfPoisoned();
        Interlocked.Increment(ref _waitingCount);
        try
        {
            try
            {
                await _semaphore.WaitAsync(_poisonCancellation.Token);
            }
            catch (OperationCanceledException) when (IsPoisoned)
            {
                ThrowIfPoisoned();
                throw;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waitingCount);
        }

        // Poison can race with a successful semaphore acquisition. Recheck before
        // the caller is allowed to launch a child.
        ThrowIfPoisoned();
    }

    internal void Release()
    {
        if (!IsPoisoned)
        {
            _semaphore.Release();
        }
    }

    internal void Poison(Exception reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (Interlocked.CompareExchange(ref _poisonReason, reason, null) is null)
        {
            _poisonCancellation.Cancel();
        }
    }

    public void Dispose()
    {
        _poisonCancellation.Dispose();
        _semaphore.Dispose();
    }

    private void ThrowIfPoisoned()
    {
        var reason = Volatile.Read(ref _poisonReason);
        if (reason is not null)
        {
            throw new InvalidOperationException(
                "CLI process launch gate is poisoned because a prior child could not be reaped.",
                reason);
        }
    }
}

/// <summary>
/// Result of a CLI subprocess invocation.
/// </summary>
internal sealed record CliCommandResult(int ExitCode, string StdOut, string StdErr);

internal sealed record CliTimeoutSnapshot(
    string ProcessState,
    int? ExitCode,
    string StandardOutputTaskState,
    string StandardErrorTaskState,
    string ProcessExitTaskState,
    CliStartupTraceSnapshot Trace)
{
    public override string ToString() =>
        $"process={ProcessState};exit={ExitCode?.ToString() ?? "none"};" +
        $"stdout={StandardOutputTaskState};stderr={StandardErrorTaskState};" +
        $"wait={ProcessExitTaskState};{Trace.ToDiagnosticString()}";
}
