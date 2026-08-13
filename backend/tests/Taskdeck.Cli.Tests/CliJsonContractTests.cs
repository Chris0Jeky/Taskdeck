using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class CliJsonContractTests
{
    [Fact]
    public async Task BoardsList_WithJson_ShouldReturnJsonArray()
    {
        await using var harness = new CliTestHarness("cli-json-contract");

        var result = await harness.RunAsync("boards list --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task BoardsCreate_WithJson_ShouldBeDiscoverableInBoardsListJson()
    {
        await using var harness = new CliTestHarness("cli-json-contract");

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
        await using var harness = new CliTestHarness("cli-json-contract");

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
        await using var harness = new CliTestHarness("cli-json-contract");

        var result = await harness.RunAsync("boards update --name Updated --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Invalid or missing --board");
    }

}
