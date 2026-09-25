using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ColumnsMissingValueTests
{
    [Fact]
    public async Task ColumnsCreate_NameFollowedByOption_ReturnsUsageErrorAndCreatesNothing()
    {
        await using var harness = new CliTestHarness("cli-columns-missing-value");

        var boardResult = await harness.RunAsync("boards create MissingValueBoard --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var result = await harness.RunAsync($"columns create --board {boardId} --name --position 0 --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--name");

        var listResult = await harness.RunAsync($"columns list --board {boardId} --json");
        listResult.ExitCode.Should().Be(0, listResult.StdErr);
        using var listDoc = JsonDocument.Parse(listResult.StdOut);
        listDoc.RootElement.EnumerateArray().Should().BeEmpty();
    }
}
