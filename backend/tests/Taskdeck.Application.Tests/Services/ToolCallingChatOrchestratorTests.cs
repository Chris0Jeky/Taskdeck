using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;

namespace Taskdeck.Application.Tests.Services;

public class ToolCallingChatOrchestratorTests
{
    private readonly Guid _boardId = Guid.NewGuid();

    private static ChatCompletionRequest MakeRequest(string userMessage) => new(
        new List<ChatCompletionMessage> { new("User", userMessage) });

    /// <summary>
    /// Creates a mock provider that returns a tool call on the first call,
    /// then a final text response on the second.
    /// </summary>
    private static Mock<ILlmProvider> CreateMockProviderWithToolCall(
        string toolName, JsonElement args, string finalResponse)
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
                if (callSequence == 1)
                {
                    return new LlmToolCompletionResult(
                        Content: null,
                        TokensUsed: 50,
                        Provider: "Test",
                        Model: "test-v1",
                        ToolCalls: new[] { new ToolCallRequest("call-1", toolName, args) },
                        IsComplete: false);
                }
                return new LlmToolCompletionResult(
                    Content: finalResponse,
                    TokensUsed: 100,
                    Provider: "Test",
                    Model: "test-v1",
                    ToolCalls: null,
                    IsComplete: true);
            });

        return mock;
    }

    private static Mock<IToolExecutor> CreateMockExecutor(string toolName, string result)
    {
        var mock = new Mock<IToolExecutor>();
        mock.SetupGet(e => e.ToolName).Returns(toolName);
        mock.Setup(e => e.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task ExecuteAsync_SimpleToolCall_CompletesInTwoRounds()
    {
        var args = JsonDocument.Parse("{\"column_name\":\"Backlog\"}").RootElement;
        var provider = CreateMockProviderWithToolCall(
            "list_cards_in_column", args, "Here are your cards.");
        var executor = CreateMockExecutor("list_cards_in_column",
            "{\"cards\":[{\"id\":\"a1b2c3d4\",\"title\":\"Test card\"}],\"total\":1,\"truncated\":false}");

        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            provider.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("What cards are in Backlog?"), _boardId);

        result.Content.Should().Be("Here are your cards.");
        result.Rounds.Should().Be(2);
        result.IsDegraded.Should().BeFalse();
        result.ToolCallLog.Should().HaveCount(1);
        result.ToolCallLog[0].ToolName.Should().Be("list_cards_in_column");
        result.ToolCallLog[0].Round.Should().Be(1);
        result.TokensUsed.Should().Be(150); // 50 + 100
    }

    [Fact]
    public async Task ExecuteAsync_DirectTextResponse_CompletesInOneRound()
    {
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmToolCompletionResult(
                Content: "I can help you manage your board.",
                TokensUsed: 80,
                Provider: "Test",
                Model: "test-v1",
                ToolCalls: null,
                IsComplete: true));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Hello"), _boardId);

        result.Content.Should().Be("I can help you manage your board.");
        result.Rounds.Should().Be(1);
        result.IsDegraded.Should().BeFalse();
        result.ToolCallLog.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsErrorInToolResult()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var provider = CreateMockProviderWithToolCall(
            "nonexistent_tool", args, "I couldn't find that tool.");

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            provider.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Do something"), _boardId);

        // Should complete (provider returns final text on round 2)
        result.Content.Should().NotBeNullOrEmpty();
        result.ToolCallLog.Should().HaveCount(1);
        result.ToolCallLog[0].IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ProviderNotSupported_ReturnsDegraded()
    {
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("Not supported"));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Hello"), _boardId);

        result.IsDegraded.Should().BeTrue();
        result.Content.Should().BeNull();
        result.DegradedReason.Should().Contain("does not support tool calling");
    }

    [Fact]
    public async Task ExecuteAsync_DegradedProviderResponse_ReturnsDegraded()
    {
        var mock = new Mock<ILlmProvider>();
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmToolCompletionResult(
                Content: "Configuration error.",
                TokensUsed: 0,
                Provider: "Test",
                Model: "test-v1",
                ToolCalls: null,
                IsComplete: true,
                IsDegraded: true,
                DegradedReason: "Invalid config."));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Hello"), _boardId);

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Be("Invalid config.");
        result.Content.Should().Be("Configuration error.");
    }

    [Fact]
    public async Task ExecuteAsync_MaxRoundsExceeded_ReturnsExhaustedResult()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var mock = new Mock<ILlmProvider>();

        // Always return a tool call, never a final response
        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmToolCompletionResult(
                Content: null,
                TokensUsed: 30,
                Provider: "Test",
                Model: "test-v1",
                ToolCalls: new[] { new ToolCallRequest("call-1", "list_board_columns", args) },
                IsComplete: false));

        var executor = CreateMockExecutor("list_board_columns", "{\"columns\":[]}");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show me everything"), _boardId);

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("Maximum tool-calling rounds");
        result.Rounds.Should().Be(ToolCallingChatOrchestrator.MaxRounds);
        result.ToolCallLog.Should().HaveCount(ToolCallingChatOrchestrator.MaxRounds);
    }

    [Fact]
    public async Task ExecuteAsync_StatusNotifier_Invoked()
    {
        var args = JsonDocument.Parse("{\"column_name\":\"Backlog\"}").RootElement;
        var provider = CreateMockProviderWithToolCall(
            "list_cards_in_column", args, "Here are your cards.");
        var executor = CreateMockExecutor("list_cards_in_column", "{\"cards\":[],\"total\":0}");

        var notifier = new Mock<IToolStatusNotifier>();
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            provider.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object,
            notifier.Object);

        await orchestrator.ExecuteAsync(MakeRequest("What cards are in Backlog?"), _boardId);

        notifier.Verify(n => n.NotifyToolStatusAsync(
            _boardId,
            "list_cards_in_column",
            It.Is<string>(s => s.Contains("Backlog")),
            1,
            ToolCallingChatOrchestrator.MaxRounds,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ToolExecutionThrows_ContinuesWithError()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var provider = CreateMockProviderWithToolCall(
            "list_board_columns", args, "Let me try something else.");

        var executor = new Mock<IToolExecutor>();
        executor.SetupGet(e => e.ToolName).Returns("list_board_columns");
        executor.Setup(e => e.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            provider.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("list columns"), _boardId);

        result.Content.Should().NotBeNullOrEmpty();
        result.ToolCallLog.Should().HaveCount(1);
        result.ToolCallLog[0].IsError.Should().BeTrue();
    }
}
