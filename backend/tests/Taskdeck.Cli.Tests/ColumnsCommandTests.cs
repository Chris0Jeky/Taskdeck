using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ColumnsCommandTests
{
    [Fact]
    public async Task ColumnsList_WithJson_RequiresBoard()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("columns list --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task ColumnsList_WithInvalidBoardId_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("columns list --board not-a-guid --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task ColumnsCreate_WithJson_CreatesColumn()
    {
        await using var harness = new CliHarness();

        var boardResult = await harness.RunAsync("boards create ColumnTestBoard --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var result = await harness.RunAsync($"columns create --board {boardId} --name Todo --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Todo");
        doc.RootElement.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task ColumnsCreate_MissingName_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var boardResult = await harness.RunAsync("boards create ColNoNameBoard --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var result = await harness.RunAsync($"columns create --board {boardId} --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--name");
    }

    [Fact]
    public async Task ColumnsCreate_MissingBoard_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("columns create --name Todo --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task ColumnsList_WithJson_ReturnsCreatedColumn()
    {
        await using var harness = new CliHarness();

        var boardResult = await harness.RunAsync("boards create ColListBoard --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var createResult = await harness.RunAsync($"columns create --board {boardId} --name InProgress --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var columnId = createDoc.RootElement.GetProperty("id").GetGuid();

        var listResult = await harness.RunAsync($"columns list --board {boardId} --json");

        listResult.ExitCode.Should().Be(0, listResult.StdErr);
        using var listDoc = JsonDocument.Parse(listResult.StdOut);
        listDoc.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .Should()
            .Contain(columnId);
    }

    [Fact]
    public async Task Columns_UnknownCommand_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("columns unknown");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown columns command");
    }

    [Fact]
    public async Task ColumnsCreate_WithPosition_SetsPosition()
    {
        await using var harness = new CliHarness();

        var boardResult = await harness.RunAsync("boards create ColPosBoard --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var result = await harness.RunAsync($"columns create --board {boardId} --name Done --position 2 --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Done");
        doc.RootElement.GetProperty("position").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ColumnsCreate_WithInvalidPosition_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var boardResult = await harness.RunAsync("boards create ColBadPosBoard --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var result = await harness.RunAsync($"columns create --board {boardId} --name BadPos --position abc --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--position");
    }

    [Fact]
    public async Task ColumnsCreate_WithInvalidWip_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var boardResult = await harness.RunAsync("boards create ColBadWipBoard --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var result = await harness.RunAsync($"columns create --board {boardId} --name BadWip --wip 0 --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--wip");
    }

    private sealed class CliHarness : IAsyncDisposable
    {
        private readonly string _repoRoot;
        private readonly string _databasePath;
        private readonly string _connectionString;

        public CliHarness()
        {
            _repoRoot = FindRepoRoot();
            _databasePath = Path.Combine(Path.GetTempPath(), $"taskdeck-cli-columns-tests-{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={_databasePath}";
        }

        public async Task<CliCommandResult> RunAsync(string arguments)
        {
            var cliDllPath = ResolveCliDllPath(_repoRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{cliDllPath}\" {arguments}",
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            startInfo.Environment["TASKDECK_CONNECTION_STRING"] = _connectionString;
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

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

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null)
            {
                var solutionPath = Path.Combine(current.FullName, "backend", "Taskdeck.sln");
                if (File.Exists(solutionPath))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root from test execution directory.");
        }

        private static string ResolveCliDllPath(string repoRoot)
        {
            var cliProjectBin = Path.Combine(repoRoot, "backend", "src", "Taskdeck.Cli", "bin");
            var debugPath = Path.Combine(cliProjectBin, "Debug", "net8.0", "Taskdeck.Cli.dll");
            if (File.Exists(debugPath))
            {
                return debugPath;
            }

            var releasePath = Path.Combine(cliProjectBin, "Release", "net8.0", "Taskdeck.Cli.dll");
            if (File.Exists(releasePath))
            {
                return releasePath;
            }

            throw new FileNotFoundException("Taskdeck.Cli.dll was not found in Debug or Release output directories.");
        }
    }

    private sealed record CliCommandResult(int ExitCode, string StdOut, string StdErr);
}
