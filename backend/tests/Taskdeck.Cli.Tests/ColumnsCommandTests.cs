using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ColumnsCommandTests
{
    [Fact]
    public async Task ColumnsList_WithJson_RequiresBoard()
    {
        await using var harness = new CliTestHarness("cli-columns");

        var result = await harness.RunAsync("columns list --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task ColumnsList_WithInvalidBoardId_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-columns");

        var result = await harness.RunAsync("columns list --board not-a-guid --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task ColumnsCreate_WithJson_CreatesColumn()
    {
        await using var harness = new CliTestHarness("cli-columns");

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
        await using var harness = new CliTestHarness("cli-columns");

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
        await using var harness = new CliTestHarness("cli-columns");

        var result = await harness.RunAsync("columns create --name Todo --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task ColumnsList_WithJson_ReturnsCreatedColumn()
    {
        await using var harness = new CliTestHarness("cli-columns");

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
        await using var harness = new CliTestHarness("cli-columns");

        var result = await harness.RunAsync("columns unknown");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown columns command");
    }

    [Fact]
    public async Task ColumnsCreate_WithPosition_SetsPosition()
    {
        await using var harness = new CliTestHarness("cli-columns");

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
        await using var harness = new CliTestHarness("cli-columns");

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
        await using var harness = new CliTestHarness("cli-columns");

        var boardResult = await harness.RunAsync("boards create ColBadWipBoard --json");
        boardResult.ExitCode.Should().Be(0, boardResult.StdErr);
        using var boardDoc = JsonDocument.Parse(boardResult.StdOut);
        var boardId = boardDoc.RootElement.GetProperty("id").GetGuid();

        var result = await harness.RunAsync($"columns create --board {boardId} --name BadWip --wip 0 --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--wip");
    }
}
