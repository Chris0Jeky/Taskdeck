using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Tests for tool-calling feature flag (LlmToolCallingSettings.Enabled) and
/// cost/token tracking integration with ILlmQuotaService.
///
/// Covers:
/// - Feature flag disabled → ChatService bypasses orchestrator, falls through to single-turn
/// - Feature flag enabled (default) → orchestrator is used for board-scoped sessions
/// - Token accumulation is reported correctly across multi-round orchestration
/// - Quota service is called with accumulated totals
/// - TruncateToolResult correctness at boundary
/// </summary>
public class ToolCallingFeatureFlagAndCostTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ChatCompletionRequest MakeRequest(string userMessage) => new(
        new List<ChatCompletionMessage> { new("User", userMessage) });

    private static Mock<IToolExecutor> CreateMockExecutor(string toolName, string result)
    {
        var mock = new Mock<IToolExecutor>();
        mock.SetupGet(e => e.ToolName).Returns(toolName);
        mock.Setup(e => e.ExecuteAsync(
                It.IsAny<ToolExecutionContext>(),
                It.IsAny<JsonElement>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    /// <summary>
    /// Creates a provider that returns one tool call on round 1 then a final response.
    /// </summary>
    private static Mock<ILlmProvider> CreateProviderWithOneToolRound(
        string toolName, int roundTokens, int finalTokens, string finalResponse)
    {
        var mock = new Mock<ILlmProvider>();
        var seq = 0;
        var args = JsonDocument.Parse("{}").RootElement;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                seq++;
                if (seq == 1)
                    return new LlmToolCompletionResult(null, roundTokens, "Test", "test-v1",
                        new[] { new ToolCallRequest("call-1", toolName, args) }, false);
                return new LlmToolCompletionResult(finalResponse, finalTokens, "Test", "test-v1",
                    null, true);
            });

        return mock;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TruncateToolResult unit tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TruncateToolResult_ShortContent_ReturnsUnchanged()
    {
        var content = "{\"columns\":[]}";
        var result = ToolCallingChatOrchestrator.TruncateToolResult(content, 8_000);
        result.Should().Be(content);
    }

    [Fact]
    public void TruncateToolResult_ZeroMaxBytes_ReturnsUnchanged()
    {
        var content = new string('X', 20_000);
        var result = ToolCallingChatOrchestrator.TruncateToolResult(content, 0);
        result.Should().Be(content);
    }

    [Fact]
    public void TruncateToolResult_NegativeMaxBytes_ReturnsUnchanged()
    {
        var content = new string('X', 20_000);
        var result = ToolCallingChatOrchestrator.TruncateToolResult(content, -1);
        result.Should().Be(content);
    }

    [Fact]
    public void TruncateToolResult_OversizedContent_TruncatesWithMarker()
    {
        var content = new string('X', 20_000);
        var result = ToolCallingChatOrchestrator.TruncateToolResult(content, 8_000);
        result.Should().EndWith("...(truncated)");
        System.Text.Encoding.UTF8.GetByteCount(result).Should().BeLessOrEqualTo(8_000);
    }

    [Fact]
    public void TruncateToolResult_ExactlyAtLimit_ReturnsUnchanged()
    {
        // Build a string whose UTF-8 byte count is exactly maxBytes
        const int maxBytes = 100;
        var content = new string('A', maxBytes); // ASCII: 1 byte per char
        var result = ToolCallingChatOrchestrator.TruncateToolResult(content, maxBytes);
        result.Should().Be(content);
    }

    [Fact]
    public void TruncateToolResult_EmptyString_ReturnsEmpty()
    {
        var result = ToolCallingChatOrchestrator.TruncateToolResult(string.Empty, 8_000);
        result.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Orchestrator token accumulation tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orchestrator_TokensAccumulated_AcrossMultipleRounds()
    {
        // 3 tool-call rounds (25 tokens each) + 1 final round (50 tokens) = 125
        var mock = new Mock<ILlmProvider>();
        var seq = 0;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                seq++;
                var cols = new[] { "Backlog", "InProgress", "Done" };
                if (seq <= 3)
                {
                    var colName = cols[seq - 1];
                    var args = JsonDocument.Parse($"{{\"column_name\":\"{colName}\"}}").RootElement;
                    return new LlmToolCompletionResult(null, 25, "Test", "test-v1",
                        new[] { new ToolCallRequest($"call-{seq}", "list_cards_in_column", args) },
                        false);
                }
                return new LlmToolCompletionResult("Found all cards.", 50, "Test", "test-v1",
                    null, true);
            });

        var executor = CreateMockExecutor("list_cards_in_column", "{\"cards\":[],\"total\":0}");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show all"), Guid.NewGuid());

        result.TokensUsed.Should().Be(125); // 3 * 25 + 50
        result.Rounds.Should().Be(4);
        result.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public async Task Orchestrator_TokensAccumulated_WhenDegradedEarly()
    {
        // Provider returns a tool call on round 1 (20 tokens) then throws on round 2
        var mock = new Mock<ILlmProvider>();
        var seq = 0;
        var args = JsonDocument.Parse("{\"column_name\":\"Backlog\"}").RootElement;

        mock.Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                seq++;
                if (seq == 1)
                    return new LlmToolCompletionResult(null, 20, "Test", "test-v1",
                        new[] { new ToolCallRequest("call-1", "list_cards_in_column", args) }, false);
                throw new HttpRequestException("Provider went down");
            });

        var executor = CreateMockExecutor("list_cards_in_column", "{\"cards\":[]}");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show backlog"), Guid.NewGuid());

        // Should still report the 20 tokens from round 1 even though it degraded
        result.IsDegraded.Should().BeTrue();
        result.TokensUsed.Should().Be(20);
    }

    [Fact]
    public async Task Orchestrator_TokenBudget_TruncatesOversizedResult()
    {
        // Set a very small budget of 50 bytes to force truncation
        var settings = new LlmToolCallingSettings { MaxToolResultBytes = 50 };

        var mock = CreateProviderWithOneToolRound(
            "list_cards_in_column", 30, 60, "Here is a summary.");

        // Tool returns a 1000-byte result — should be truncated
        var largeResult = new string('X', 1000);
        var executor = CreateMockExecutor("list_cards_in_column", largeResult);
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            mock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object,
            statusNotifier: null,
            settings: settings);

        var result = await orchestrator.ExecuteAsync(MakeRequest("Show everything"), Guid.NewGuid());

        result.IsDegraded.Should().BeFalse();
        // The tool call log should show truncated result in the summary
        result.ToolCallLog.Should().HaveCount(1);
        result.ToolCallLog[0].ResultSummary.Should().Contain("truncated");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ChatService feature flag tests
    // These tests wire a real orchestrator and verify the feature flag bypasses it.
    // ─────────────────────────────────────────────────────────────────────────

    private static (ChatService service, Mock<ILlmProvider> provider, Mock<IUnitOfWork> uow,
        Mock<IChatSessionRepository> sessionRepo, Mock<IChatMessageRepository> msgRepo)
        BuildChatService(
            ToolCallingChatOrchestrator? orchestrator = null,
            LlmToolCallingSettings? settings = null)
    {
        var provider = new Mock<ILlmProvider>();
        provider
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult("Single-turn response", 10, false));

        var uow = new Mock<IUnitOfWork>();
        var sessionRepo = new Mock<IChatSessionRepository>();
        var msgRepo = new Mock<IChatMessageRepository>();
        var columnRepo = new Mock<IColumnRepository>();
        var userRepo = new Mock<IUserRepository>();

        uow.SetupGet(u => u.ChatSessions).Returns(sessionRepo.Object);
        uow.SetupGet(u => u.ChatMessages).Returns(msgRepo.Object);
        uow.SetupGet(u => u.Columns).Returns(columnRepo.Object);
        uow.SetupGet(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        msgRepo.Setup(r => r.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage msg, CancellationToken _) => msg);

        var planner = new Mock<IAutomationPlannerService>();
        var proposalSvc = new Mock<IAutomationProposalService>();
        var policyEngine = new Mock<IAutomationPolicyEngine>();
        var notificationSvc = new Mock<INotificationService>();
        notificationSvc
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var service = new ChatService(
            uow.Object,
            provider.Object,
            planner.Object,
            proposalSvc.Object,
            policyEngine.Object,
            notificationSvc.Object,
            toolCallingOrchestrator: orchestrator,
            toolCallingSettings: settings);

        return (service, provider, uow, sessionRepo, msgRepo);
    }

    [Fact]
    public async Task FeatureFlag_Disabled_BypassesOrchestratorAndUsesSingleTurn()
    {
        // The orchestrator provider mock will throw if called — should NOT be called.
        var orchestratorProviderMock = new Mock<ILlmProvider>();
        orchestratorProviderMock
            .Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Orchestrator must NOT be called when disabled"));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            orchestratorProviderMock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var disabledSettings = new LlmToolCallingSettings { Enabled = false };

        var (service, singleTurnProvider, _, sessionRepo, _) =
            BuildChatService(orchestrator, disabledSettings);

        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Board session", boardId);
        sessionRepo.Setup(r => r.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("List my cards"), default);

        result.IsSuccess.Should().BeTrue();
        // Single-turn response should be used
        result.Value.Content.Should().Be("Single-turn response");
        // Orchestrator should never have been called
        orchestratorProviderMock.Verify(
            p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        // Single-turn provider should have been called
        singleTurnProvider.Verify(
            p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FeatureFlag_Enabled_UsesOrchestratorForBoardScopedSession()
    {
        // Orchestrator provider returns a direct text response (no tool calls)
        var orchestratorProviderMock = new Mock<ILlmProvider>();
        orchestratorProviderMock
            .Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmToolCompletionResult(
                "Orchestrator response", 50, "Test", "test-v1", null, true));

        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var orchestrator = new ToolCallingChatOrchestrator(
            orchestratorProviderMock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var enabledSettings = new LlmToolCallingSettings { Enabled = true };

        var (service, singleTurnProvider, _, sessionRepo, _) =
            BuildChatService(orchestrator, enabledSettings);

        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Board session", boardId);
        sessionRepo.Setup(r => r.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Hello board"), default);

        result.IsSuccess.Should().BeTrue();
        // Orchestrator was invoked (even when no tools were called, it still handles the request)
        orchestratorProviderMock.Verify(
            p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FeatureFlag_Disabled_NoOrchestratorAtAll_StillWorks()
    {
        // No orchestrator provided, feature flag disabled — should use single-turn
        var disabledSettings = new LlmToolCallingSettings { Enabled = false };
        var (service, singleTurnProvider, _, sessionRepo, _) =
            BuildChatService(orchestrator: null, settings: disabledSettings);

        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Board session", boardId);
        sessionRepo.Setup(r => r.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeTrue();
        singleTurnProvider.Verify(
            p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FeatureFlag_DisabledForNonBoardSession_AlwaysUsesSingleTurn()
    {
        // Non-board sessions never use the orchestrator regardless of the flag
        var disabledSettings = new LlmToolCallingSettings { Enabled = false };
        var (service, singleTurnProvider, _, sessionRepo, _) =
            BuildChatService(orchestrator: null, settings: disabledSettings);

        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "No-board session"); // No boardId
        sessionRepo.Setup(r => r.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeTrue();
        singleTurnProvider.Verify(
            p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FeatureFlag_DefaultSettings_IsEnabled()
    {
        // Default LlmToolCallingSettings should have Enabled = true
        var defaults = new LlmToolCallingSettings();
        defaults.Enabled.Should().BeTrue();
        defaults.MaxToolResultBytes.Should().Be(8_000);

        // Verify default settings are used when null is passed to the orchestrator
        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());
        var provider = new Mock<ILlmProvider>();
        var orchestrator = new ToolCallingChatOrchestrator(
            provider.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object,
            statusNotifier: null,
            settings: null); // null → should use defaults

        // The orchestrator should be created without throwing
        orchestrator.Should().NotBeNull();

        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Quota service integration with cost tracking
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CostTracking_QuotaServiceCalledWithAccumulatedTokens_WhenToolCallsInvoked()
    {
        var orchestratorProviderMock = new Mock<ILlmProvider>();
        var seq = 0;
        var args = JsonDocument.Parse("{\"column_name\":\"Backlog\"}").RootElement;

        // Round 1: 30 tokens for tool call; Round 2: 70 tokens for final response
        orchestratorProviderMock
            .Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                seq++;
                if (seq == 1)
                    return new LlmToolCompletionResult(null, 30, "TestProvider", "test-v1",
                        new[] { new ToolCallRequest("call-1", "list_cards_in_column", args) }, false);
                return new LlmToolCompletionResult("Here are your cards.", 70, "TestProvider", "test-v1",
                    null, true);
            });

        var executor = CreateMockExecutor("list_cards_in_column", "{\"cards\":[{\"id\":\"a1\",\"title\":\"Card 1\"}],\"total\":1}");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });
        var orchestrator = new ToolCallingChatOrchestrator(
            orchestratorProviderMock.Object, registry,
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var quotaService = new Mock<ILlmQuotaService>();
        quotaService
            .Setup(q => q.CheckQuotaAsync(It.IsAny<Guid>(), It.IsAny<Domain.Enums.LlmSurface>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaCheckResultDto(true, null, long.MaxValue, long.MaxValue));
        quotaService
            .Setup(q => q.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<Domain.Enums.LlmSurface>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        var sessionRepo = new Mock<IChatSessionRepository>();
        var msgRepo = new Mock<IChatMessageRepository>();
        var columnRepo = new Mock<IColumnRepository>();
        var userRepo = new Mock<IUserRepository>();
        uow.SetupGet(u => u.ChatSessions).Returns(sessionRepo.Object);
        uow.SetupGet(u => u.ChatMessages).Returns(msgRepo.Object);
        uow.SetupGet(u => u.Columns).Returns(columnRepo.Object);
        uow.SetupGet(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        msgRepo.Setup(r => r.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage msg, CancellationToken _) => msg);

        var singleTurnProvider = new Mock<ILlmProvider>();
        var planner = new Mock<IAutomationPlannerService>();
        var proposalSvc = new Mock<IAutomationProposalService>();
        var policyEngine = new Mock<IAutomationPolicyEngine>();
        var notificationSvc = new Mock<INotificationService>();
        notificationSvc
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var service = new ChatService(
            uow.Object,
            singleTurnProvider.Object,
            planner.Object,
            proposalSvc.Object,
            policyEngine.Object,
            notificationSvc.Object,
            quotaService: quotaService.Object,
            toolCallingOrchestrator: orchestrator);

        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Board session", boardId);
        sessionRepo.Setup(r => r.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("List cards in Backlog"), default);

        result.IsSuccess.Should().BeTrue();

        // Quota service should be called once with the TOTAL accumulated tokens (30 + 70 = 100)
        quotaService.Verify(
            q => q.RecordUsageAsync(
                userId,
                Domain.Enums.LlmSurface.Chat,
                "TestProvider",
                "test-v1",
                100, // Total accumulated tokens across all rounds
                0,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
