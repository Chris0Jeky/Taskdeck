using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class CardsCommandTests
{
    [Fact]
    public async Task CardsAdd_WithJson_CreatesCard()
    {
        await using var harness = new CliTestHarness("cli-cards");
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
        await using var harness = new CliTestHarness("cli-cards");

        var result = await harness.RunAsync("cards add --column 00000000-0000-0000-0000-000000000001 --title \"No Board\" --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task CardsAdd_MissingColumn_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-cards");
        var (boardId, _) = await harness.CreateBoardAndColumnAsync();

        var result = await harness.RunAsync($"cards add --board {boardId} --title \"No Column\" --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--column");
    }

    [Fact]
    public async Task CardsAdd_MissingTitle_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-cards");
        var (boardId, columnId) = await harness.CreateBoardAndColumnAsync();

        var result = await harness.RunAsync($"cards add --board {boardId} --column {columnId} --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--title");
    }

    [Fact]
    public async Task CardsAdd_WithDescription_IncludesDescription()
    {
        await using var harness = new CliTestHarness("cli-cards");
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
        await using var harness = new CliTestHarness("cli-cards");
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
        await using var harness = new CliTestHarness("cli-cards");

        var result = await harness.RunAsync("cards list --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--board");
    }

    [Fact]
    public async Task CardsMove_MovesCardToTargetColumn()
    {
        await using var harness = new CliTestHarness("cli-cards");
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
        await using var harness = new CliTestHarness("cli-cards");

        var result = await harness.RunAsync("cards move --target-column 00000000-0000-0000-0000-000000000001 --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--card");
    }

    [Fact]
    public async Task CardsMove_MissingTargetColumn_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-cards");

        var result = await harness.RunAsync("cards move --card 00000000-0000-0000-0000-000000000001 --json");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--target-column");
    }

    [Fact]
    public async Task Cards_UnknownCommand_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-cards");

        var result = await harness.RunAsync("cards unknown");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown cards command");
    }
}
