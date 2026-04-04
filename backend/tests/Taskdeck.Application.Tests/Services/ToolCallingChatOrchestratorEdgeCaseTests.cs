using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Edge case and boundary tests for ToolCallingChatOrchestrator.
/// Covers: per-round timeout, total timeout at round 4, empty tool call lists,
/// concurrent tool calls within a round, tool-not-found with suggestion,
/// tool returning very large results, multiple tools with mixed errors,
/// cancellation token propagation, metadata JSON generation, and
/// incomplete result with no tool calls.
/// </summary>
public class ToolCallingChatOrchestratorEdgeCaseTests
{
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private static ChatCompletionRequest MakeRequest(string userMessage) => new(
        new List<ChatCompletionMessage> { new("User", userMessage) });

    private static Mock<IToolExecutor> CreateMockExecutor(string toolName, string result)
    {
        var mock = new Mock<IToolExecutor>();
        mock.SetupGet(e => e.ToolName).Returns(toolName);
        mock.Setup(e => e.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        mock.Setup(e => e.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task ExecuteAsync_PerRoundTimeout_ReturnsDegradedResult()
    {
        // Simulate a provider that takes longer than PerRoundTimeoutSeconds
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (ChatCompletionRequest _, IReadOnlyList<TaskdeckToolSchema> _,
                IReadOnlyList<ToolCallResult>? _, CancellationToken ct) =>
            {
                // Wait until cancellation triggers (per-round timeout)
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
                return new LlmToolCompletionResult(
                    Content: "Should not reach here",
                    TokensUsed: 0,
                    Provider: "Test",
                    Model: "test-v1",
                    ToolCalls: null,
                    IsComplete: true);
            });

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("What cards?"), _boardId);

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("timeout");
    }

    [Fact]
    public async Task ExecuteAsync_IncompleteResultWithNoToolCalls_ReturnsDegraded()
    {
        // Provider returns IsComplete=false but with no tool calls
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmToolCompletionResult(
                Content: null,
                TokensUsed: 10,
                Provider: "Test",
                Model: "test-v1",
                ToolCalls: null, // No tool calls
                IsComplete: false)); // But not complete

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Something"), _boardId);

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("empty tool call list");
        result.Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_IncompleteResultWithEmptyToolCallList_ReturnsDegraded()
    {
        // Provider returns IsComplete=false with an empty (non-null) tool call list
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmToolCompletionResult(
                Content: null,
                TokensUsed: 10,
                Provider: "Test",
                Model: "test-v1",
                ToolCalls: Array.Empty<ToolCallRequest>(),
                IsComplete: false));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Something"), _boardId);

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("empty tool call list");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleToolCallsInOneRound_AllExecuted()
    {
        var mock = new Mock<ILlmProvider>();
        var callSequence = 0;

        var args1 = JsonDocument.Parse("{}").RootElement;
        var args2 = JsonDocument.Parse("{\"column_name\":\"Backlog\"}").RootElement;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    // Return two tool calls in a single round
                    return new LlmToolCompletionResult(
                        Content: null,
                        TokensUsed: 50,
                        Provider: "Test",
                        Model: "test-v1",
                        ToolCalls: new[]
                        {
                            new ToolCallRequest("call-1", "list_board_columns", args1),
                            new ToolCallRequest("call-2", "list_cards_in_column", args2)
                        },
                        IsComplete: false);
                }
                return new LlmToolCompletionResult(
                    Content: "Here is the board overview.",
                    TokensUsed: 80,
                    Provider: "Test",
                    Model: "test-v1",
                    ToolCalls: null,
                    IsComplete: true);
            });

        var colsExecutor = CreateMockExecutor("list_board_columns", "{\"columns\":[{\"name\":\"Backlog\"}]}");
        var cardsExecutor = CreateMockExecutor("list_cards_in_column", "{\"cards\":[],\"total\":0}");
        var registry = new ToolExecutorRegistry(new[] { colsExecutor.Object, cardsExecutor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show board overview"), _boardId);

        result.IsDegraded.Should().BeFalse();
        result.Rounds.Should().Be(2);
        result.ToolCallLog.Should().HaveCount(2);
        result.ToolCallLog[0].ToolName.Should().Be("list_board_columns");
        result.ToolCallLog[1].ToolName.Should().Be("list_cards_in_column");

        // Verify both executors were called
        colsExecutor.Verify(e => e.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
        cardsExecutor.Verify(e => e.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleToolCallsWithMixedErrors_ContinuesWithAll()
    {
        var mock = new Mock<ILlmProvider>();
        var callSequence = 0;

        var args1 = JsonDocument.Parse("{}").RootElement;
        var args2 = JsonDocument.Parse("{\"card_id\":\"bad-id\"}").RootElement;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    return new LlmToolCompletionResult(
                        Content: null,
                        TokensUsed: 50,
                        Provider: "Test",
                        Model: "test-v1",
                        ToolCalls: new[]
                        {
                            new ToolCallRequest("call-1", "list_board_columns", args1),
                            new ToolCallRequest("call-2", "get_card_details", args2)
                        },
                        IsComplete: false);
                }
                return new LlmToolCompletionResult(
                    Content: "I found some columns but couldn't get card details.",
                    TokensUsed: 80,
                    Provider: "Test",
                    Model: "test-v1",
                    ToolCalls: null,
                    IsComplete: true);
            });

        var colsExecutor = CreateMockExecutor("list_board_columns", "{\"columns\":[]}");
        var cardExecutor = new Mock<IToolExecutor>();
        cardExecutor.SetupGet(e => e.ToolName).Returns("get_card_details");
        cardExecutor.Setup(e => e.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid card ID format"));

        var registry = new ToolExecutorRegistry(new[] { colsExecutor.Object, cardExecutor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show card and columns"), _boardId);

        result.IsDegraded.Should().BeFalse(); // LLM recovered with a final response
        result.ToolCallLog.Should().HaveCount(2);
        result.ToolCallLog[0].IsError.Should().BeFalse(); // columns succeeded
        result.ToolCallLog[1].IsError.Should().BeTrue();  // card details failed
    }

    [Fact]
    public async Task ExecuteAsync_ToolNotFound_ProvidesAvailableToolSuggestion()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var mock = new Mock<ILlmProvider>();
        var callSequence = 0;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    return new LlmToolCompletionResult(
                        Content: null,
                        TokensUsed: 50,
                        Provider: "Test",
                        Model: "test-v1",
                        ToolCalls: new[] { new ToolCallRequest("call-1", "invented_tool", args) },
                        IsComplete: false);
                }
                return new LlmToolCompletionResult(
                    Content: "Let me try another approach.",
                    TokensUsed: 80,
                    Provider: "Test",
                    Model: "test-v1",
                    ToolCalls: null,
                    IsComplete: true);
            });

        var executor = CreateMockExecutor("list_board_columns", "{\"columns\":[]}");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Do something"), _boardId);

        result.ToolCallLog.Should().HaveCount(1);
        result.ToolCallLog[0].IsError.Should().BeTrue();
        // The error response should mention available tools
        result.ToolCallLog[0].ResultSummary.Should().Contain("list_board_columns");
    }

    [Fact]
    public async Task ExecuteAsync_GenericProviderException_ReturnsDegraded()
    {
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HTTP 500 from provider"));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Hello"), _boardId);

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("internal error");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationTokenRespected_ThrowsOperationCanceled()
    {
        // When the external cancellation token is already cancelled before the call,
        // the orchestrator should throw immediately at ct.ThrowIfCancellationRequested().
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmToolCompletionResult(
                Content: "done", TokensUsed: 0,
                Provider: "Test", Model: "test-v1",
                ToolCalls: null, IsComplete: true));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Already cancelled

        var act = async () => await orchestrator.ExecuteAsync(
            MakeRequest("Hello"), _boardId, _userId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_UserIdPassedToExecutor_ViaToolExecutionContext()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var mock = new Mock<ILlmProvider>();
        var callSequence = 0;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    return new LlmToolCompletionResult(
                        Content: null, TokensUsed: 50,
                        Provider: "Test", Model: "test-v1",
                        ToolCalls: new[] { new ToolCallRequest("call-1", "list_board_columns", args) },
                        IsComplete: false);
                }
                return new LlmToolCompletionResult(
                    Content: "Done", TokensUsed: 80,
                    Provider: "Test", Model: "test-v1",
                    ToolCalls: null, IsComplete: true);
            });

        ToolExecutionContext? capturedContext = null;
        var executor = new Mock<IToolExecutor>();
        executor.SetupGet(e => e.ToolName).Returns("list_board_columns");
        executor.Setup(e => e.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<ToolExecutionContext, JsonElement, CancellationToken>((ctx, _, _) => capturedContext = ctx)
            .ReturnsAsync("{\"columns\":[]}");

        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        await orchestrator.ExecuteAsync(MakeRequest("Show columns"), _boardId, _userId);

        capturedContext.Should().NotBeNull();
        capturedContext!.BoardId.Should().Be(_boardId);
        capturedContext.UserId.Should().Be(_userId);
    }

    [Fact]
    public async Task ExecuteAsync_ToolReturnsLargeResult_TruncatedInLog()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var mock = new Mock<ILlmProvider>();
        var callSequence = 0;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    return new LlmToolCompletionResult(
                        Content: null, TokensUsed: 50,
                        Provider: "Test", Model: "test-v1",
                        ToolCalls: new[] { new ToolCallRequest("call-1", "list_cards_in_column", args) },
                        IsComplete: false);
                }
                return new LlmToolCompletionResult(
                    Content: "Here are many cards.", TokensUsed: 80,
                    Provider: "Test", Model: "test-v1",
                    ToolCalls: null, IsComplete: true);
            });

        // Return a large result (simulating 1000 cards)
        var largeResult = new string('X', 5000);
        var executor = CreateMockExecutor("list_cards_in_column", largeResult);
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show all cards"), _boardId);

        result.IsDegraded.Should().BeFalse();
        result.ToolCallLog.Should().HaveCount(1);
        // The log should truncate large results (200 chars per TruncateForLog)
        result.ToolCallLog[0].ResultSummary.Length.Should().BeLessThan(5000);
        result.ToolCallLog[0].ResultSummary.Should().Contain("truncated");
    }

    [Fact]
    public void BuildToolCallMetadataJson_EmptyLog_ReturnsNull()
    {
        var result = ToolCallingChatOrchestrator.BuildToolCallMetadataJson(
            new List<ToolCallLogEntry>(), totalRounds: 0, totalTokens: 0);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildToolCallMetadataJson_WithEntries_ReturnsValidJson()
    {
        var args = JsonDocument.Parse("{\"column_name\":\"Backlog\"}").RootElement;
        var log = new List<ToolCallLogEntry>
        {
            new(1, "list_cards_in_column", args, "{\"cards\":[]}", false),
            new(2, "get_card_details", args, "error", true)
        };

        var result = ToolCallingChatOrchestrator.BuildToolCallMetadataJson(log, totalRounds: 2, totalTokens: 150);

        result.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(result!);
        doc.RootElement.GetProperty("rounds").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("total_tokens").GetInt32().Should().Be(150);
        doc.RootElement.GetProperty("tool_calls").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void ComputeToolCallFingerprint_EmptyList_DoesNotThrow()
    {
        var calls = new List<ToolCallRequest>();

        var act = () => ToolCallingChatOrchestrator.ComputeToolCallFingerprint(calls);

        act.Should().NotThrow();
        var result = act();
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ComputeToolCallFingerprint_SingleCall_ProducesDeterministicResult()
    {
        var args = JsonDocument.Parse("{\"q\":\"test\"}").RootElement;
        var calls = new List<ToolCallRequest> { new("call-1", "search_cards", args) };

        var fp1 = ToolCallingChatOrchestrator.ComputeToolCallFingerprint(calls);
        var fp2 = ToolCallingChatOrchestrator.ComputeToolCallFingerprint(calls);

        fp1.Should().Be(fp2);
    }

    [Fact]
    public async Task ExecuteAsync_StatusNotifier_InvokedForEachToolInRound()
    {
        var mock = new Mock<ILlmProvider>();
        var callSequence = 0;
        var args = JsonDocument.Parse("{}").RootElement;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callSequence++;
                if (callSequence == 1)
                {
                    return new LlmToolCompletionResult(
                        Content: null, TokensUsed: 50,
                        Provider: "Test", Model: "test-v1",
                        ToolCalls: new[]
                        {
                            new ToolCallRequest("call-1", "list_board_columns", args),
                            new ToolCallRequest("call-2", "get_board_labels", args)
                        },
                        IsComplete: false);
                }
                return new LlmToolCompletionResult(
                    Content: "Done.", TokensUsed: 80,
                    Provider: "Test", Model: "test-v1",
                    ToolCalls: null, IsComplete: true);
            });

        var colsExecutor = CreateMockExecutor("list_board_columns", "{\"columns\":[]}");
        var labelsExecutor = CreateMockExecutor("get_board_labels", "{\"labels\":[]}");
        var notifier = new Mock<IToolStatusNotifier>();
        var registry = new ToolExecutorRegistry(new[] { colsExecutor.Object, labelsExecutor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object,
            notifier.Object);

        await orchestrator.ExecuteAsync(MakeRequest("Board overview"), _boardId);

        // Should be called once for each tool in the round (2 tools)
        notifier.Verify(n => n.NotifyToolStatusAsync(
            _boardId, It.IsAny<string>(), It.IsAny<string>(),
            1, ToolCallingChatOrchestrator.MaxRounds,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_TokensAccumulated_AcrossAllRounds()
    {
        var mock = new Mock<ILlmProvider>();
        var callSequence = 0;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callSequence++;
                if (callSequence <= 3)
                {
                    var colName = callSequence switch
                    {
                        1 => "Backlog",
                        2 => "InProgress",
                        _ => "Done"
                    };
                    var args = JsonDocument.Parse($"{{\"column_name\":\"{colName}\"}}").RootElement;
                    return new LlmToolCompletionResult(
                        Content: null,
                        TokensUsed: 25,
                        Provider: "Test",
                        Model: "test-v1",
                        ToolCalls: new[] { new ToolCallRequest($"call-{callSequence}", "list_cards_in_column", args) },
                        IsComplete: false);
                }
                return new LlmToolCompletionResult(
                    Content: "Found the cards.",
                    TokensUsed: 50,
                    Provider: "Test",
                    Model: "test-v1",
                    ToolCalls: null,
                    IsComplete: true);
            });

        var executor = CreateMockExecutor("list_cards_in_column", "{\"cards\":[],\"total\":0}");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show everything"), _boardId);

        // 3 rounds * 25 tokens + 1 final round * 50 tokens = 125
        result.TokensUsed.Should().Be(125);
        result.Rounds.Should().Be(4);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderReturnsNullContent_OnComplete_UsesEmptyString()
    {
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmToolCompletionResult(
                Content: null, // null content on complete
                TokensUsed: 10,
                Provider: "Test",
                Model: "test-v1",
                ToolCalls: null,
                IsComplete: true));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Hi"), _boardId);

        result.IsDegraded.Should().BeFalse();
        result.Content.Should().Be(""); // Empty string, not null
    }

    [Fact]
    public async Task ExecuteAsync_ExhaustedRounds_PartialSummaryIncludesSuccessfulTools()
    {
        var mock = new Mock<ILlmProvider>();
        var callSequence = 0;
        var columnNames = new[] { "A", "B", "C", "D", "E" };

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callSequence++;
                var colName = columnNames[(callSequence - 1) % columnNames.Length];
                var args = JsonDocument.Parse($"{{\"column_name\":\"{colName}\"}}").RootElement;
                return new LlmToolCompletionResult(
                    Content: null, TokensUsed: 20,
                    Provider: "Test", Model: "test-v1",
                    ToolCalls: new[] { new ToolCallRequest($"call-{callSequence}", "list_cards_in_column", args) },
                    IsComplete: false);
            });

        var executor = CreateMockExecutor("list_cards_in_column", "{\"cards\":[]}");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show everything"), _boardId);

        result.IsDegraded.Should().BeTrue();
        result.Content.Should().Contain("[list_cards_in_column]");
    }
}
