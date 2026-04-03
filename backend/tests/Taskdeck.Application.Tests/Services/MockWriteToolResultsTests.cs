using System.Text.Json;
using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

public class MockWriteToolResultsTests
{
    [Fact]
    public void ProposeCreateCard_ReturnsValidProposalJson()
    {
        var result = MockToolResults.ProposeCreateCard("Fix bug", "Backlog");
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Fix bug");
        doc.RootElement.GetProperty("risk").GetString().Should().Be("Low");
    }

    [Fact]
    public void ProposeMoveCard_ReturnsValidProposalJson()
    {
        var result = MockToolResults.ProposeMoveCard("a1b2c3d4", "Done");
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("a1b2c3d4");
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Done");
    }

    [Fact]
    public void ProposeArchiveCard_ReturnsValidProposalJson()
    {
        var result = MockToolResults.ProposeArchiveCard("a1b2c3d4");
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Archive");
        doc.RootElement.GetProperty("risk").GetString().Should().Be("Medium");
    }

    [Fact]
    public void ProposeUpdateCard_ReturnsValidProposalJson()
    {
        var result = MockToolResults.ProposeUpdateCard("a1b2c3d4");
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Update");
    }

    [Fact]
    public void ProposeBulkMove_ReturnsValidProposalJson()
    {
        var result = MockToolResults.ProposeBulkMove("Done", "Archive");
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Done").And.Contain("Archive");
        doc.RootElement.GetProperty("card_count").GetInt32().Should().Be(3);
    }

    [Fact]
    public void ProposeCreateColumn_ReturnsValidProposalJson()
    {
        var result = MockToolResults.ProposeCreateColumn("Review");
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("summary").GetString().Should().Contain("Review");
    }

    [Theory]
    [InlineData("propose_create_card", """{"title": "Test"}""")]
    [InlineData("propose_move_card", """{"card_id": "a1b2c3d4", "target_column": "Done"}""")]
    [InlineData("propose_archive_card", """{"card_id": "a1b2c3d4"}""")]
    [InlineData("propose_update_card", """{"card_id": "a1b2c3d4"}""")]
    [InlineData("propose_bulk_move", """{"source_column": "Done", "target_column": "Archive"}""")]
    [InlineData("propose_create_column", """{"name": "Review"}""")]
    public void Execute_WriteTools_ReturnProposalId(string toolName, string argsJson)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        var result = MockToolResults.Execute(toolName, args);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("proposal_id").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("create card called 'Fix login'", "propose_create_card")]
    [InlineData("move a1b2c3d4 to Done", "propose_move_card")]
    [InlineData("archive a1b2c3d4", "propose_archive_card")]
    [InlineData("move all cards from Done to Archive", "propose_bulk_move")]
    [InlineData("create column called 'Review'", "propose_create_column")]
    public void MockDispatcher_MatchesWriteToolPatterns(string message, string expectedTool)
    {
        var request = MockToolCallDispatcher.TryDispatch(message);
        request.Should().NotBeNull();
        request!.ToolName.Should().Be(expectedTool);
    }
}
