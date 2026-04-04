using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

public class GeminiToolCallingParseTests
{
    private readonly GeminiLlmProvider _provider;

    public GeminiToolCallingParseTests()
    {
        _provider = new GeminiLlmProvider(
            new HttpClient(),
            new LlmProviderSettings
            {
                Gemini = new GeminiProviderSettings { ApiKey = "test", Model = "gemini-2.5-flash" }
            },
            new Mock<ILogger<GeminiLlmProvider>>().Object);
    }

    [Fact]
    public void ParseToolCallingResponse_FunctionCall_ExtractsToolCall()
    {
        var json = """
            {
                "candidates": [{
                    "content": {
                        "parts": [{
                            "functionCall": {
                                "id": "fc_xyz",
                                "name": "list_cards_in_column",
                                "args": { "column_name": "Backlog" }
                            }
                        }]
                    }
                }],
                "usageMetadata": { "totalTokenCount": 120 }
            }
            """;

        var result = _provider.ParseToolCallingResponse(json);

        result.IsComplete.Should().BeFalse();
        result.ToolCalls.Should().NotBeNull();
        result.ToolCalls!.Count.Should().Be(1);
        result.ToolCalls[0].CallId.Should().Be("fc_xyz");
        result.ToolCalls[0].ToolName.Should().Be("list_cards_in_column");
        result.ToolCalls[0].Arguments.GetProperty("column_name").GetString().Should().Be("Backlog");
        result.TokensUsed.Should().Be(120);
        result.Provider.Should().Be("Gemini");
    }

    [Fact]
    public void ParseToolCallingResponse_TextResponse_ExtractsContent()
    {
        var json = """
            {
                "candidates": [{
                    "content": {
                        "parts": [{
                            "text": "Here are the cards in your Backlog."
                        }]
                    }
                }],
                "usageMetadata": { "totalTokenCount": 80 }
            }
            """;

        var result = _provider.ParseToolCallingResponse(json);

        result.IsComplete.Should().BeTrue();
        result.Content.Should().StartWith("Here are the cards");
        result.ToolCalls.Should().BeNull();
        result.TokensUsed.Should().Be(80);
    }

    [Fact]
    public void ParseToolCallingResponse_MultipleFunctionCalls_ExtractsAll()
    {
        var json = """
            {
                "candidates": [{
                    "content": {
                        "parts": [
                            {
                                "functionCall": {
                                    "id": "fc_1",
                                    "name": "list_board_columns",
                                    "args": {}
                                }
                            },
                            {
                                "functionCall": {
                                    "id": "fc_2",
                                    "name": "get_board_labels",
                                    "args": {}
                                }
                            }
                        ]
                    }
                }],
                "usageMetadata": { "totalTokenCount": 90 }
            }
            """;

        var result = _provider.ParseToolCallingResponse(json);

        result.IsComplete.Should().BeFalse();
        result.ToolCalls.Should().HaveCount(2);
        result.ToolCalls![0].ToolName.Should().Be("list_board_columns");
        result.ToolCalls[1].ToolName.Should().Be("get_board_labels");
    }

    [Fact]
    public void ParseToolCallingResponse_NoId_GeneratesSyntheticId()
    {
        var json = """
            {
                "candidates": [{
                    "content": {
                        "parts": [{
                            "functionCall": {
                                "name": "list_board_columns",
                                "args": {}
                            }
                        }]
                    }
                }],
                "usageMetadata": { "totalTokenCount": 50 }
            }
            """;

        var result = _provider.ParseToolCallingResponse(json);

        result.ToolCalls.Should().NotBeNull();
        result.ToolCalls![0].CallId.Should().NotBeNullOrEmpty();
        result.ToolCalls[0].CallId.Should().StartWith("gemini-");
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
    public void ParseToolCallingResponse_NoCandidates_ReturnsDegraded()
    {
        var result = _provider.ParseToolCallingResponse("{}");
        result.IsComplete.Should().BeTrue();
        result.IsDegraded.Should().BeTrue();
    }

    [Fact]
    public void BuildToolCallingPayload_PreviousResults_ReplayOriginalArguments()
    {
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "What cards?") });
        var tools = new List<TaskdeckToolSchema>();

        var originalArgs = JsonDocument.Parse("{\"column_name\":\"Backlog\"}").RootElement;
        var previousResults = new List<ToolCallResult>
        {
            new("fc_1", "list_cards_in_column", "{\"cards\":[]}", false, originalArgs)
        };

        var payload = _provider.BuildToolCallingPayload(request, tools, previousResults);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var contents = doc.RootElement.GetProperty("contents");

        // Find the synthetic model message with functionCall parts
        JsonElement? modelMsg = null;
        foreach (var msg in contents.EnumerateArray())
        {
            if (msg.TryGetProperty("role", out var role) &&
                role.GetString() == "model" &&
                msg.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("functionCall", out _))
                    {
                        modelMsg = msg;
                        break;
                    }
                }
            }
        }

        modelMsg.Should().NotBeNull("payload should contain a synthetic model message with functionCall");
        var callPart = modelMsg!.Value.GetProperty("parts")[0].GetProperty("functionCall");
        callPart.GetProperty("name").GetString().Should().Be("list_cards_in_column");
        callPart.GetProperty("args").GetProperty("column_name").GetString().Should().Be("Backlog");
    }

    [Fact]
    public void BuildToolCallingPayload_PreviousResultsWithoutArguments_FallsBackToEmptyObject()
    {
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "columns?") });
        var tools = new List<TaskdeckToolSchema>();

        // ToolCallResult constructed without explicit Arguments (backward compat)
        var previousResults = new List<ToolCallResult>
        {
            new("fc_1", "list_board_columns", "{\"columns\":[]}", false)
        };

        var payload = _provider.BuildToolCallingPayload(request, tools, previousResults);

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var contents = doc.RootElement.GetProperty("contents");

        JsonElement? modelMsg = null;
        foreach (var msg in contents.EnumerateArray())
        {
            if (msg.TryGetProperty("role", out var role) &&
                role.GetString() == "model" &&
                msg.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("functionCall", out _))
                    {
                        modelMsg = msg;
                        break;
                    }
                }
            }
        }

        modelMsg.Should().NotBeNull();
        var callPart = modelMsg!.Value.GetProperty("parts")[0].GetProperty("functionCall");
        var args = callPart.GetProperty("args");
        // Should be an empty object (no properties)
        args.ValueKind.Should().Be(JsonValueKind.Object);
        args.EnumerateObject().Should().BeEmpty();
    }
}
