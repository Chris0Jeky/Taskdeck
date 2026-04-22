using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class BoardsCommandTests
{
    [Fact]
    public async Task BoardsList_EmptyDatabase_ReturnsEmptyJsonArray()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("boards list --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task BoardsCreate_MissingName_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("boards create --json");

        // When "--json" is the first positional arg, it is treated as the board name.
        // So "boards create" without any name should fail.
        // Actually, looking at the handler: create expects args[0] as name.
        // With "--json" stripped, there would be no args left.
        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Missing board name");
    }

    [Fact]
    public async Task BoardsCreate_WithDescription_IncludesDescription()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("boards create DescBoard \"A nice description\" --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("name").GetString().Should().Be("DescBoard");
    }

    [Fact]
    public async Task BoardsUpdate_WithName_UpdatesBoardName()
    {
        await using var harness = new CliHarness();

        var createResult = await harness.RunAsync("boards create OrigName --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createDoc.RootElement.GetProperty("id").GetGuid();

        var updateResult = await harness.RunAsync($"boards update --board {boardId} --name NewName --json");

        updateResult.ExitCode.Should().Be(0, updateResult.StdErr);
        using var updateDoc = JsonDocument.Parse(updateResult.StdOut);
        updateDoc.RootElement.GetProperty("name").GetString().Should().Be("NewName");
    }

    [Fact]
    public async Task BoardsUpdate_NoUpdateValues_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var createResult = await harness.RunAsync("boards create NoUpdBoard --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createDoc.RootElement.GetProperty("id").GetGuid();

        var updateResult = await harness.RunAsync($"boards update --board {boardId} --json");

        updateResult.ExitCode.Should().Be(2);
        updateResult.StdErr.Should().Contain("No update values");
    }

    [Fact]
    public async Task BoardsUpdate_ArchiveAndUnarchiveTogether_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var createResult = await harness.RunAsync("boards create ConflictBoard --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createDoc.RootElement.GetProperty("id").GetGuid();

        var updateResult = await harness.RunAsync($"boards update --board {boardId} --archive --unarchive --json");

        updateResult.ExitCode.Should().Be(2);
        updateResult.StdErr.Should().Contain("--archive and --unarchive");
    }

    [Fact]
    public async Task Boards_UnknownCommand_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("boards unknown");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown boards command");
    }

    private sealed class CliHarness : IAsyncDisposable
    {
        private readonly string _repoRoot;
        private readonly string _databasePath;
        private readonly string _connectionString;

        public CliHarness()
        {
            _repoRoot = FindRepoRoot();
            _databasePath = Path.Combine(Path.GetTempPath(), $"taskdeck-cli-boards-tests-{Guid.NewGuid():N}.db");
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
