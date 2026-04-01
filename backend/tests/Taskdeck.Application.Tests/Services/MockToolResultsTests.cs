using System.Text.Json;
using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

public class MockToolResultsTests
{
    [Fact]
    public void ListBoardColumns_ReturnsValidJson()
    {
        var result = MockToolResults.ListBoardColumns();
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("columns").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void ListCardsInColumn_IncludesColumnNameInCardTitles()
    {
        var result = MockToolResults.ListCardsInColumn("Backlog");
        var doc = JsonDocument.Parse(result);
        var cards = doc.RootElement.GetProperty("cards");
        cards.GetArrayLength().Should().Be(3);
        cards[0].GetProperty("title").GetString().Should().Contain("Backlog");
    }

    [Fact]
    public void GetCardDetails_IncludesCardId()
    {
        var result = MockToolResults.GetCardDetails("a1b2c3d4");
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("id").GetString().Should().Be("a1b2c3d4");
    }

    [Fact]
    public void SearchCards_IncludesQueryInTitles()
    {
        var result = MockToolResults.SearchCards("login bug");
        var doc = JsonDocument.Parse(result);
        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().Should().Be(2);
        results[0].GetProperty("title").GetString().Should().Contain("login bug");
    }

    [Fact]
    public void GetBoardLabels_ReturnsValidJson()
    {
        var result = MockToolResults.GetBoardLabels();
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("labels").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void Execute_UnknownTool_ReturnsError()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var result = MockToolResults.Execute("unknown_tool", args);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Unknown tool");
    }

    [Fact]
    public void Execute_ListCardsInColumn_DelegatesToCorrectMethod()
    {
        var args = JsonDocument.Parse("{\"column_name\": \"Done\"}").RootElement;
        var result = MockToolResults.Execute("list_cards_in_column", args);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("cards")[0].GetProperty("title").GetString().Should().Contain("Done");
    }
}
