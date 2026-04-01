using System.Text.Json;
using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

public class MockLlmProviderToolCallingTests
{
    private readonly MockLlmProvider _provider = new();
    private readonly IReadOnlyList<TaskdeckToolSchema> _emptyTools = Array.Empty<TaskdeckToolSchema>();

    [Fact]
    public async Task CompleteWithToolsAsync_MatchingPattern_ReturnsToolCall()
    {
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "What cards are in Backlog?")
            });

        var result = await _provider.CompleteWithToolsAsync(request, _emptyTools);

        result.IsComplete.Should().BeFalse();
        result.ToolCalls.Should().NotBeNull();
        result.ToolCalls!.Count.Should().Be(1);
        result.ToolCalls[0].ToolName.Should().Be("list_cards_in_column");
        result.Provider.Should().Be("Mock");
        result.Model.Should().Be("mock-tool-v1");
    }

    [Fact]
    public async Task CompleteWithToolsAsync_NoMatch_ReturnsFinalResponse()
    {
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "Hello, how are you?")
            });

        var result = await _provider.CompleteWithToolsAsync(request, _emptyTools);

        result.IsComplete.Should().BeTrue();
        result.Content.Should().NotBeNullOrEmpty();
        result.ToolCalls.Should().BeNull();
    }

    [Fact]
    public async Task CompleteWithToolsAsync_WithPreviousResults_ReturnsSummary()
    {
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "What cards are in Backlog?")
            });

        var previousResults = new List<ToolCallResult>
        {
            new("call-1", "list_cards_in_column",
                MockToolResults.ListCardsInColumn("Backlog"), false)
        };

        var result = await _provider.CompleteWithToolsAsync(request, _emptyTools, previousResults);

        result.IsComplete.Should().BeTrue();
        result.Content.Should().Contain("tool results");
        result.ToolCalls.Should().BeNull();
    }

    [Fact]
    public async Task CompleteWithToolsAsync_EmptyMessage_ReturnsFinalResponse()
    {
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage>());

        var result = await _provider.CompleteWithToolsAsync(request, _emptyTools);

        result.IsComplete.Should().BeTrue();
        result.ToolCalls.Should().BeNull();
    }
}
