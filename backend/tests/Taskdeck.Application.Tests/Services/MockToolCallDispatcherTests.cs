using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

public class MockToolCallDispatcherTests
{
    [Theory]
    [InlineData("What cards are in Backlog?", "list_cards_in_column")]
    [InlineData("cards in my Done column", "list_cards_in_column")]
    [InlineData("what cards are in the In Progress?", "list_cards_in_column")]
    public void TryDispatch_CardsInColumn_ReturnsCorrectTool(string message, string expectedTool)
    {
        var result = MockToolCallDispatcher.TryDispatch(message);
        result.Should().NotBeNull();
        result!.ToolName.Should().Be(expectedTool);
    }

    [Theory]
    [InlineData("list columns", "list_board_columns")]
    [InlineData("show all columns", "list_board_columns")]
    [InlineData("what columns do I have", "list_board_columns")]
    [InlineData("which columns exist", "list_board_columns")]
    public void TryDispatch_ListColumns_ReturnsCorrectTool(string message, string expectedTool)
    {
        var result = MockToolCallDispatcher.TryDispatch(message);
        result.Should().NotBeNull();
        result!.ToolName.Should().Be(expectedTool);
    }

    [Theory]
    [InlineData("details of card a1b2c3d4", "get_card_details")]
    [InlineData("card details for e5f6a7b8", "get_card_details")]
    public void TryDispatch_CardDetails_ReturnsCorrectTool(string message, string expectedTool)
    {
        var result = MockToolCallDispatcher.TryDispatch(message);
        result.Should().NotBeNull();
        result!.ToolName.Should().Be(expectedTool);
    }

    [Theory]
    [InlineData("search for login bug", "search_cards")]
    [InlineData("find onboarding tasks", "search_cards")]
    [InlineData("look up login", "search_cards")]
    public void TryDispatch_Search_ReturnsCorrectTool(string message, string expectedTool)
    {
        var result = MockToolCallDispatcher.TryDispatch(message);
        result.Should().NotBeNull();
        result!.ToolName.Should().Be(expectedTool);
    }

    [Theory]
    [InlineData("list labels", "get_board_labels")]
    [InlineData("show all labels", "get_board_labels")]
    [InlineData("what labels do I have", "get_board_labels")]
    public void TryDispatch_Labels_ReturnsCorrectTool(string message, string expectedTool)
    {
        var result = MockToolCallDispatcher.TryDispatch(message);
        result.Should().NotBeNull();
        result!.ToolName.Should().Be(expectedTool);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("how are you")]
    [InlineData("")]
    [InlineData(null)]
    public void TryDispatch_NoMatch_ReturnsNull(string? message)
    {
        var result = MockToolCallDispatcher.TryDispatch(message!);
        result.Should().BeNull();
    }

    [Fact]
    public void TryDispatch_CardsInColumn_IncludesColumnNameInArguments()
    {
        var result = MockToolCallDispatcher.TryDispatch("what cards are in Backlog?");
        result.Should().NotBeNull();
        result!.Arguments.GetProperty("column_name").GetString().Should().Be("Backlog");
    }

    [Fact]
    public void TryDispatch_CardDetails_IncludesCardIdInArguments()
    {
        var result = MockToolCallDispatcher.TryDispatch("details of card a1b2c3d4");
        result.Should().NotBeNull();
        result!.Arguments.GetProperty("card_id").GetString().Should().Be("a1b2c3d4");
    }

    [Fact]
    public void TryDispatch_Search_IncludesQueryInArguments()
    {
        var result = MockToolCallDispatcher.TryDispatch("search for login bug");
        result.Should().NotBeNull();
        result!.Arguments.GetProperty("query").GetString().Should().Be("login bug");
    }

    [Fact]
    public void TryDispatch_AssignsCallId()
    {
        var result = MockToolCallDispatcher.TryDispatch("list columns");
        result.Should().NotBeNull();
        result!.CallId.Should().NotBeNullOrEmpty();
    }
}
