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

    private readonly string _databasePath;
    private readonly string _connectionString;

    public CliTestHarness(string dbPrefix = "taskdeck-cli-tests")
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"{dbPrefix}-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_databasePath}";
    }

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

    public async Task<CliCommandResult> RunAsync(string arguments)
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
            UseShellExecute = false
        };

        startInfo.Environment["TASKDECK_CONNECTION_STRING"] = _connectionString;
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = new Process { StartInfo = startInfo };
        using var cts = new CancellationTokenSource(ProcessTimeout);

        process.Start();

        string stdOut, stdErr;
        try
        {
            stdOut = await process.StandardOutput.ReadToEndAsync(cts.Token);
            stdErr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException(
                $"CLI process did not exit within {ProcessTimeout.TotalSeconds}s. Args: {arguments}");
        }

        return new CliCommandResult(process.ExitCode, stdOut.Trim(), stdErr.Trim());
    }

    public ValueTask DisposeAsync()
    {
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm", $"{_databasePath}-journal" })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
        }

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
