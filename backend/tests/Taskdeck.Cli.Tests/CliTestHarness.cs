using System.Diagnostics;
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
    private static readonly SemaphoreSlim ProcessLaunchSemaphore = new(
        initialCount: Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
        maxCount: Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    private readonly string _dataDirectory;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly bool _provisionEncryptionKey;
    private readonly TimeSpan _processTimeout;

    /// <param name="provisionEncryptionKey">
    /// When true (default) the harness injects a test connector encryption key via
    /// the environment, matching a configured machine. When false the harness
    /// simulates a CLEAN machine: no key is supplied, so the CLI must bootstrap
    /// one itself.
    /// </param>
    public CliTestHarness(
        string dbPrefix = "taskdeck-cli-tests",
        bool provisionEncryptionKey = true,
        TimeSpan? processTimeout = null)
    {
        // Each harness gets its own data directory so the SQLite file -- and any
        // appsettings.local.json written by the CLI's first-run bootstrap -- are
        // isolated from other tests and cleaned up on dispose.
        _dataDirectory = Path.Combine(Path.GetTempPath(), $"{dbPrefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDirectory);
        _databasePath = Path.Combine(_dataDirectory, "taskdeck.db");
        _connectionString = $"Data Source={_databasePath}";
        _provisionEncryptionKey = provisionEncryptionKey;
        _processTimeout = processTimeout ?? ProcessTimeout;
        if (_processTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTimeout));
        }
    }

    /// <summary>
    /// Directory that holds this harness's SQLite database. On a fresh-machine run
    /// (<c>provisionEncryptionKey: false</c>) the CLI's first-run bootstrap writes
    /// <c>appsettings.local.json</c> here.
    /// </summary>
    public string DataDirectory => _dataDirectory;
    public string DatabasePath => _databasePath;
    internal int? LastStartedProcessId { get; private set; }

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
        await ProcessLaunchSemaphore.WaitAsync();
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

            using var process = new Process { StartInfo = startInfo };
            using var cts = new CancellationTokenSource(_processTimeout);

            process.Start();
            LastStartedProcessId = process.Id;

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await Task.WhenAll(
                    standardOutputTask,
                    standardErrorTask,
                    process.WaitForExitAsync(cts.Token));
            }
            catch (OperationCanceledException)
            {
                await TerminateAndReapAsync(process);
                throw new TimeoutException(
                    $"CLI process did not exit within {_processTimeout.TotalSeconds}s. Args: {arguments}");
            }

            return new CliCommandResult(
                process.ExitCode,
                standardOutputTask.Result.Trim(),
                standardErrorTask.Result.Trim());
        }
        finally
        {
            ProcessLaunchSemaphore.Release();
        }
    }

    private static async Task TerminateAndReapAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The child exited between the cancellation check and Kill.
        }

        using var terminationCts = new CancellationTokenSource(TerminationTimeout);
        try
        {
            await process.WaitForExitAsync(terminationCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Preserve the original timeout while bounding cleanup; the runner will still
            // report a child that refuses to die rather than silently extending the deadline.
        }
    }

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
/// Result of a CLI subprocess invocation.
/// </summary>
internal sealed record CliCommandResult(int ExitCode, string StdOut, string StdErr);
