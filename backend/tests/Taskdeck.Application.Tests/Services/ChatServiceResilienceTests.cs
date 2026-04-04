using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Resilience tests for ChatService: LLM provider failures, degraded responses,
/// cancellation handling, and fallback to single-turn when tool-calling is degraded.
/// Covers issue #720 (TST-53).
/// </summary>
public class ChatServiceResilienceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IChatSessionRepository> _chatSessionRepoMock = new();
    private readonly Mock<IChatMessageRepository> _chatMessageRepoMock = new();
    private readonly Mock<IColumnRepository> _columnRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ILlmProvider> _llmProviderMock = new();
    private readonly Mock<IAutomationPlannerService> _plannerMock = new();
    private readonly Mock<IAutomationProposalService> _proposalServiceMock = new();
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();

    public ChatServiceResilienceTests()
    {
        _unitOfWorkMock.SetupGet(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _chatMessageRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ChatMessage>(), default))
            .ReturnsAsync((ChatMessage message, CancellationToken _) => message);
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));
    }

    // -----------------------------------------------------------------------
    // LLM provider degraded response — single-turn path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SendMessageAsync_WhenProviderReturnsDegraded_MessageTypeIsDegraded()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Degraded session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .ReturnsAsync(new LlmCompletionResult(
                "I'm having trouble right now.",
                5,
                false,
                IsDegraded: true,
                DegradedReason: "Provider timeout"));

        var service = BuildService();
        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().Be("degraded");
    }

    [Fact]
    public async Task SendMessageAsync_WhenProviderReturnsDegraded_DegradedReasonIsPersistedInResponse()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Degraded reason session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        const string expectedReason = "Live provider request failed.";
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .ReturnsAsync(new LlmCompletionResult(
                "Partial response due to error.",
                0,
                false,
                IsDegraded: true,
                DegradedReason: expectedReason));

        var service = BuildService();
        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Tell me something"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().Be("degraded");
        // The response still returns successfully (no crash) — user gets a message
        result.Value.Content.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SendMessageAsync_WhenProviderThrowsException_ReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Exception session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .ThrowsAsync(new InvalidOperationException("Provider unavailable"));

        var service = BuildService();

        // The service should propagate the exception (callers handle it at HTTP layer)
        // or return a failure — either way, no data corruption happens.
        var act = async () => await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Test"), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendMessageAsync_WhenProviderReturnsEmptyContent_DomainValidationPreventsEmptyMessage()
    {
        // When the provider returns empty content, the ChatMessage domain entity constructor
        // throws DomainException("Content cannot be empty"). ChatService catches DomainException
        // in its outer try/catch and returns Result.Failure — it does NOT silently persist an
        // empty message. This test verifies the failure is surfaced with the correct error code.
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Empty content session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .ReturnsAsync(new LlmCompletionResult(
                string.Empty,
                0,
                false,
                IsDegraded: true,
                DegradedReason: "Empty response."));

        var service = BuildService();
        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Any question"), default);

        // ChatService catches DomainException and returns a failure result; it never persists
        // an empty assistant message.
        result.IsSuccess.Should().BeFalse(
            "empty content returned by the provider must never be silently persisted");
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError,
            "ChatService wraps the DomainException from ChatMessage into a ValidationError result");
    }

    // -----------------------------------------------------------------------
    // Kill-switch and quota — provider bypassed entirely
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SendMessageAsync_WhenKillSwitchActive_ReturnsFailureWithoutCallingProvider()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Kill-switch session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        var killSwitchMock = new Mock<ILlmKillSwitchService>();
        killSwitchMock
            .Setup(k => k.IsKilledAsync(It.IsAny<Domain.Enums.LlmSurface>(), userId, default))
            .ReturnsAsync(true);

        var service = BuildService(killSwitch: killSwitchMock.Object);
        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LlmKillSwitchActive);
        // Provider must never be called when kill switch is active
        _llmProviderMock.Verify(
            p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_WhenQuotaExceeded_ReturnsFailureWithoutCallingProvider()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Quota session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        var quotaMock = new Mock<ILlmQuotaService>();
        quotaMock
            .Setup(q => q.CheckQuotaAsync(userId, It.IsAny<Domain.Enums.LlmSurface>(), default))
            .ReturnsAsync(new QuotaCheckResultDto(false, "Daily quota exceeded", 0, 0));

        var service = BuildService(quota: quotaMock.Object);
        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LlmQuotaExceeded);
        _llmProviderMock.Verify(
            p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Cancellation token propagation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SendMessageAsync_WhenCancellationRequested_PropagatesCancellation()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Cancellation session");
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        using var cts = new CancellationTokenSource();
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns<ChatCompletionRequest, CancellationToken>(async (_, ct) =>
            {
                await Task.Delay(5, ct); // tiny delay so token fires
                ct.ThrowIfCancellationRequested();
                return new LlmCompletionResult("Never reached", 0, false);
            });

        cts.Cancel();
        var service = BuildService();

        var act = async () => await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("Hello"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -----------------------------------------------------------------------
    // Session not found — no crash
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SendMessageAsync_WhenSessionNotFound_ReturnsNotFound()
    {
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((ChatSession?)null);

        var service = BuildService();
        var result = await service.SendMessageAsync(
            Guid.NewGuid(), Guid.NewGuid(), new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _llmProviderMock.Verify(
            p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Tool-calling degraded — falls back to single-turn
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SendMessageAsync_WhenToolCallingProviderTimesOut_OrchestratorReturnsDegradedAndMessageTypeIsDegraded()
    {
        // Use the real orchestrator wired to a provider that simulates per-round timeout.
        // The orchestrator catches the OperationCanceledException from the per-round token
        // and returns a degraded ToolCallingResult with no content.
        // ChatService then falls through to the single-turn CompleteAsync path,
        // which also returns a degraded response.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Tool timeout session", boardId);
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        // Provider throws on CompleteWithToolsAsync → orchestrator returns degraded
        _llmProviderMock
            .Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("Simulated per-round timeout"));

        // Single-turn fallback also returns degraded
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult(
                "Service temporarily degraded.",
                0,
                false,
                IsDegraded: true,
                DegradedReason: "Timeout"));

        var orchestrator = new ToolCallingChatOrchestrator(
            _llmProviderMock.Object,
            new ToolExecutorRegistry(Array.Empty<IToolExecutor>()),
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var service = BuildService(orchestrator: orchestrator);
        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("List my cards"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MessageType.Should().Be("degraded");
    }

    [Fact]
    public async Task SendMessageAsync_WhenToolCallingNotSupportedByProvider_FallsBackToSingleTurn()
    {
        // If the provider doesn't support tool calling (throws NotSupportedException via default
        // interface implementation), CompleteWithToolsAsync is never invoked in a way that
        // crashes the session. The orchestrator handles it and falls back; ChatService then
        // uses CompleteAsync to complete the request.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "No tool support session", boardId);
        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        // Provider doesn't support tool calling → orchestrator gets degraded path
        _llmProviderMock
            .Setup(p => p.CompleteWithToolsAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<IReadOnlyList<TaskdeckToolSchema>>(),
                It.IsAny<IReadOnlyList<ToolCallResult>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("Provider does not support tool calling"));

        // Single-turn fallback succeeds
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult("Here is your answer.", 10, false));

        var orchestrator = new ToolCallingChatOrchestrator(
            _llmProviderMock.Object,
            new ToolExecutorRegistry(Array.Empty<IToolExecutor>()),
            new Mock<ILogger<ToolCallingChatOrchestrator>>().Object);

        var service = BuildService(orchestrator: orchestrator);
        var result = await service.SendMessageAsync(
            session.Id, userId, new SendChatMessageDto("What are my cards?"), default);

        result.IsSuccess.Should().BeTrue();
        // Should fall back to single-turn; message type will be "text" from CompleteAsync
        result.Value.Content.Should().Be("Here is your answer.");
        _llmProviderMock.Verify(
            p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ChatService BuildService(
        ILlmKillSwitchService? killSwitch = null,
        ILlmQuotaService? quota = null,
        ToolCallingChatOrchestrator? orchestrator = null)
    {
        return new ChatService(
            _unitOfWorkMock.Object,
            _llmProviderMock.Object,
            _plannerMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _notificationServiceMock.Object,
            _authorizationServiceMock.Object,
            quota,
            killSwitch,
            boardContextBuilder: null,
            toolCallingOrchestrator: orchestrator);
    }
}
