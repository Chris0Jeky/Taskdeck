using System.Text.Json;
using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

public class ToolCallMetadataTests
{
    [Fact]
    public void BuildToolCallMetadataJson_EmptyLog_ReturnsNull()
    {
        var result = ToolCallingChatOrchestrator.BuildToolCallMetadataJson(
            Array.Empty<ToolCallLogEntry>(), 0, 0);
        result.Should().BeNull();
    }

    [Fact]
    public void BuildToolCallMetadataJson_WithEntries_ReturnsValidJson()
    {
        var log = new List<ToolCallLogEntry>
        {
            new(1, "list_cards_in_column",
                JsonDocument.Parse("{\"column_name\":\"Done\"}").RootElement.Clone(),
                "3 cards found", false),
            new(2, "propose_bulk_move",
                JsonDocument.Parse("{\"source_column\":\"Done\",\"target_column\":\"Archive\"}").RootElement.Clone(),
                "Proposal p-123 created", false)
        };

        var result = ToolCallingChatOrchestrator.BuildToolCallMetadataJson(log, 3, 4200);
        result.Should().NotBeNull();

        var doc = JsonDocument.Parse(result!);
        doc.RootElement.GetProperty("rounds").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("total_tokens").GetInt32().Should().Be(4200);
        doc.RootElement.GetProperty("tool_calls").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void BuildToolCallMetadataJson_PreservesToolNames()
    {
        var log = new List<ToolCallLogEntry>
        {
            new(1, "propose_create_card",
                JsonDocument.Parse("{\"title\":\"Test\"}").RootElement.Clone(),
                "Proposal created", false)
        };

        var result = ToolCallingChatOrchestrator.BuildToolCallMetadataJson(log, 1, 100);
        var doc = JsonDocument.Parse(result!);
        var firstCall = doc.RootElement.GetProperty("tool_calls")[0];
        firstCall.GetProperty("tool").GetString().Should().Be("propose_create_card");
        firstCall.GetProperty("round").GetInt32().Should().Be(1);
        firstCall.GetProperty("is_error").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void BuildToolCallMetadataJson_ErrorEntries_MarkedCorrectly()
    {
        var log = new List<ToolCallLogEntry>
        {
            new(1, "propose_move_card",
                JsonDocument.Parse("{\"card_id\":\"00000000\"}").RootElement.Clone(),
                "Card not found", true)
        };

        var result = ToolCallingChatOrchestrator.BuildToolCallMetadataJson(log, 1, 50);
        var doc = JsonDocument.Parse(result!);
        doc.RootElement.GetProperty("tool_calls")[0].GetProperty("is_error").GetBoolean().Should().BeTrue();
    }
}
