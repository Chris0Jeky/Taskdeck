using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class CardsCommandTests
{
    [Fact]
    public async Task CardsAdd_WithJson_CreatesCard()
    {
        await using var harness = new CliHarness();
        var (boardId, columnId) = await harness.CreateBoardAndColumnAsync();

        var result = await harness.RunAsync($"cards add --board {boardId} --column {columnId} --title \"Test Card\" --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Test Card");
        doc.RootElement.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CardsAdd_MissingBoard_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("cards add --column 00000000-0000-0000-0000-000000000001 --title \"No Board\" --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task CardsAdd_MissingColumn_ReturnsUsageError()
    {
        await using var harness = new CliHarness();
        var (boardId, _) = await harness.CreateBoardAndColumnAsync();

        var result = await harness.RunAsync($"cards add --board {boardId} --title \"No Column\" --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--column");
    }

    [Fact]
    public async Task CardsAdd_MissingTitle_ReturnsUsageError()
    {
        await using var harness = new CliHarness();
        var (boardId, columnId) = await harness.CreateBoardAndColumnAsync();

        var result = await harness.RunAsync($"cards add --board {boardId} --column {columnId} --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--title");
    }

    [Fact]
    public async Task CardsAdd_WithDescription_IncludesDescription()
    {
        await using var harness = new CliHarness();
        var (boardId, columnId) = await harness.CreateBoardAndColumnAsync();

        var result = await harness.RunAsync($"cards add --board {boardId} --column {columnId} --title \"Described\" --description \"A description\" --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Described");
        doc.RootElement.GetProperty("description").GetString().Should().Be("A description");
    }

    [Fact]
    public async Task CardsList_WithJson_ReturnsCreatedCards()
    {
        await using var harness = new CliHarness();
        var (boardId, columnId) = await harness.CreateBoardAndColumnAsync();

        await harness.RunAsync($"cards add --board {boardId} --column {columnId} --title \"Card A\" --json");
        await harness.RunAsync($"cards add --board {boardId} --column {columnId} --title \"Card B\" --json");

        var result = await harness.RunAsync($"cards list --board {boardId} --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        var titles = doc.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("title").GetString())
            .ToList();
        titles.Should().Contain("Card A");
        titles.Should().Contain("Card B");
    }

    [Fact]
    public async Task CardsList_MissingBoard_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("cards list --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task CardsMove_MovesCardToTargetColumn()
    {
        await using var harness = new CliHarness();
        var (boardId, columnId) = await harness.CreateBoardAndColumnAsync();

        // Create a second column
        var col2Result = await harness.RunAsync($"columns create --board {boardId} --name Done --json");
        col2Result.ExitCode.Should().Be(0, col2Result.StdErr);
        using var col2Doc = JsonDocument.Parse(col2Result.StdOut);
        var targetColumnId = col2Doc.RootElement.GetProperty("id").GetGuid();

        // Create a card in the first column
        var cardResult = await harness.RunAsync($"cards add --board {boardId} --column {columnId} --title \"Movable\" --json");
        cardResult.ExitCode.Should().Be(0, cardResult.StdErr);
        using var cardDoc = JsonDocument.Parse(cardResult.StdOut);
        var cardId = cardDoc.RootElement.GetProperty("id").GetGuid();

        // Move the card
        var moveResult = await harness.RunAsync($"cards move --card {cardId} --target-column {targetColumnId} --json");

        moveResult.ExitCode.Should().Be(0, moveResult.StdErr);
        using var moveDoc = JsonDocument.Parse(moveResult.StdOut);
        moveDoc.RootElement.GetProperty("columnId").GetGuid().Should().Be(targetColumnId);
    }

    [Fact]
    public async Task CardsMove_MissingCard_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("cards move --target-column 00000000-0000-0000-0000-000000000001 --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--card");
    }

    [Fact]
    public async Task CardsMove_MissingTargetColumn_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("cards move --card 00000000-0000-0000-0000-000000000001 --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--target-column");
    }

    [Fact]
    public async Task Cards_UnknownCommand_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("cards unknown");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown cards command");
    }

    private sealed class CliHarness : IAsyncDisposable
    {
        private readonly string _repoRoot;
        private readonly string _databasePath;
        private readonly string _connectionString;

        public CliHarness()
        {
            _repoRoot = FindRepoRoot();
            _databasePath = Path.Combine(Path.GetTempPath(), $"taskdeck-cli-cards-tests-{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={_databasePath}";
        }

        public async Task<(Guid BoardId, Guid ColumnId)> CreateBoardAndColumnAsync()
        {
            var boardResult = await RunAsync("boards create CardTestBoard --json");
            boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
            using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
            var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

            var columnResult = await RunAsync($"columns create --board {boardId} --name Todo --json");
            columnResult.ExitCode.Should().Be(0, columnResult.StdErr);
            using var columnDoc = JsonDocument.Parse(columnResult.StdOut);
            var columnId = columnDoc.RootElement.GetProperty("id").GetGuid();

            return (boardId, columnId);
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
