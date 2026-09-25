using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class BoardsMissingValueTests
{
    [Fact]
    public async Task BoardsUpdate_NameMissingValueWithArchive_ReturnsUsageErrorAndLeavesBoardUnchanged()
    {
        await using var harness = new CliTestHarness("cli-boards-missing-value");

        var createResult = await harness.RunAsync("boards create OrigBoard --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createDoc.RootElement.GetProperty("id").GetGuid();

        var updateResult = await harness.RunAsync($"boards update --board {boardId} --name --archive --json");

        updateResult.ExitCode.Should().Be(2);
        updateResult.StdErr.Should().Contain("--name");

        var listResult = await harness.RunAsync("boards list --json");
        listResult.ExitCode.Should().Be(0, listResult.StdErr);
        using var listDoc = JsonDocument.Parse(listResult.StdOut);
        var board = listDoc.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == boardId);
        board.GetProperty("name").GetString().Should().Be("OrigBoard");
        board.GetProperty("isArchived").GetBoolean().Should().BeFalse();
    }
}
