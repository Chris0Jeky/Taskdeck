using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class CliJsonContractTests
{
    [Fact]
    public async Task BoardsList_WithJson_ShouldReturnJsonArray()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("boards list --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task BoardsCreate_WithJson_ShouldBeDiscoverableInBoardsListJson()
    {
        await using var harness = new CliHarness();

        var createResult = await harness.RunAsync("boards create ContractBoard --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createdDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createdDoc.RootElement.GetProperty("id").GetGuid();

        var listResult = await harness.RunAsync("boards list --json");
        listResult.ExitCode.Should().Be(0, listResult.StdErr);
        using var listDoc = JsonDocument.Parse(listResult.StdOut);
        listDoc.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .Should()
            .Contain(boardId);
    }

    [Fact]
    public async Task CardsList_WithJson_ShouldReturnCreatedCard()
    {
        await using var harness = new CliHarness();

        var createBoardResult = await harness.RunAsync("boards create JsonCardsBoard --json");
        createBoardResult.ExitCode.Should().Be(0, createBoardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(createBoardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var createColumnResult = await harness.RunAsync($"columns create --board {boardId} --name Todo --json");
        createColumnResult.ExitCode.Should().Be(0, createColumnResult.StdErr);
        using var columnDoc = JsonDocument.Parse(createColumnResult.StdOut);
        var columnId = columnDoc.RootElement.GetProperty("id").GetGuid();

        var createCardResult = await harness.RunAsync($"cards add --board {boardId} --column {columnId} --title JsonCard --json");
        createCardResult.ExitCode.Should().Be(0, createCardResult.StdErr);
        using var cardDoc = JsonDocument.Parse(createCardResult.StdOut);
        var cardId = cardDoc.RootElement.GetProperty("id").GetGuid();

        var listCardsResult = await harness.RunAsync($"cards list --board {boardId} --json");
        listCardsResult.ExitCode.Should().Be(0, listCardsResult.StdErr);
        using var listDoc = JsonDocument.Parse(listCardsResult.StdOut);
        listDoc.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .Should()
            .Contain(cardId);
    }

    [Fact]
    public async Task BoardsUpdate_MissingBoardArgument_ShouldReturnUsageExitCode()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("boards update --name Updated --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Invalid or missing --board");
    }

    private sealed class CliHarness : IAsyncDisposable
    {
        private readonly string _repoRoot;
        private readonly string _databasePath;
        private readonly string _connectionString;

        public CliHarness()
        {
            _repoRoot = FindRepoRoot();
            _databasePath = Path.Combine(Path.GetTempPath(), $"taskdeck-cli-tests-{Guid.NewGuid():N}.db");
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
            try
            {
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                }
            }
            catch (IOException)
            {
                // No-op for teardown cleanup issues.
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
