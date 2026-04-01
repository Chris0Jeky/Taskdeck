using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

public class OpenAiToolCallingParseTests
{
    private readonly OpenAiLlmProvider _provider;

    public OpenAiToolCallingParseTests()
    {
        _provider = new OpenAiLlmProvider(
            new HttpClient(),
            new LlmProviderSettings
            {
                OpenAi = new OpenAiProviderSettings { ApiKey = "test", Model = "gpt-4o-mini" }
            },
            new Mock<ILogger<OpenAiLlmProvider>>().Object);
    }

    [Fact]
    public void ParseToolCallingResponse_ToolCallResponse_ExtractsToolCalls()
    {
        var json = """
            {
                "choices": [{
                    "message": {
                        "role": "assistant",
                        "tool_calls": [
                            {
                                "id": "call_abc123",
                                "type": "function",
                                "function": {
                                    "name": "list_cards_in_column",
                                    "arguments": "{\"column_name\": \"Backlog\"}"
                                }
                            }
                        ]
                    },
                    "finish_reason": "tool_calls"
                }],
                "usage": { "total_tokens": 150 }
            }
            """;

        var result = _provider.ParseToolCallingResponse(json);

        result.IsComplete.Should().BeFalse();
        result.ToolCalls.Should().NotBeNull();
        result.ToolCalls!.Count.Should().Be(1);
        result.ToolCalls[0].CallId.Should().Be("call_abc123");
        result.ToolCalls[0].ToolName.Should().Be("list_cards_in_column");
        result.ToolCalls[0].Arguments.GetProperty("column_name").GetString().Should().Be("Backlog");
        result.TokensUsed.Should().Be(150);
        result.Provider.Should().Be("OpenAI");
    }

    [Fact]
    public void ParseToolCallingResponse_FinalTextResponse_ExtractsContent()
    {
        var json = """
            {
                "choices": [{
                    "message": {
                        "role": "assistant",
                        "content": "Here are the cards in your Backlog: ..."
                    },
                    "finish_reason": "stop"
                }],
                "usage": { "total_tokens": 200 }
            }
            """;

        var result = _provider.ParseToolCallingResponse(json);

        result.IsComplete.Should().BeTrue();
        result.Content.Should().StartWith("Here are the cards");
        result.ToolCalls.Should().BeNull();
        result.TokensUsed.Should().Be(200);
    }

    [Fact]
    public void ParseToolCallingResponse_MultipleToolCalls_ExtractsAll()
    {
        var json = """
            {
                "choices": [{
                    "message": {
                        "role": "assistant",
                        "tool_calls": [
                            {
                                "id": "call_1",
                                "type": "function",
                                "function": {
                                    "name": "list_board_columns",
                                    "arguments": "{}"
                                }
                            },
                            {
                                "id": "call_2",
                                "type": "function",
                                "function": {
                                    "name": "get_board_labels",
                                    "arguments": "{}"
                                }
                            }
                        ]
                    },
                    "finish_reason": "tool_calls"
                }],
                "usage": { "total_tokens": 100 }
            }
            """;

        var result = _provider.ParseToolCallingResponse(json);

        result.IsComplete.Should().BeFalse();
        result.ToolCalls.Should().HaveCount(2);
        result.ToolCalls![0].ToolName.Should().Be("list_board_columns");
        result.ToolCalls[1].ToolName.Should().Be("get_board_labels");
    }

    [Fact]
    public void ParseToolCallingResponse_EmptyBody_ReturnsDegraded()
    {
        var result = _provider.ParseToolCallingResponse("");
        result.IsComplete.Should().BeTrue();
        result.IsDegraded.Should().BeTrue();
    }

    [Fact]
    public void ParseToolCallingResponse_InvalidJson_ReturnsDegraded()
    {
        var result = _provider.ParseToolCallingResponse("not json");
        result.IsComplete.Should().BeTrue();
        result.IsDegraded.Should().BeTrue();
    }

    [Fact]
    public void ParseToolCallingResponse_NoChoices_ReturnsDegraded()
    {
        var result = _provider.ParseToolCallingResponse("{}");
        result.IsComplete.Should().BeTrue();
        result.IsDegraded.Should().BeTrue();
    }
}
