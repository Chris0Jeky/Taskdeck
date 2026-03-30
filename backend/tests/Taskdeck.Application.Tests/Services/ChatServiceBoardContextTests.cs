using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ChatServiceBoardContextTests
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
    private readonly Mock<IBoardContextBuilder> _boardContextBuilderMock = new();

    private readonly ChatService _service;

    public ChatServiceBoardContextTests()
    {
        _unitOfWorkMock.SetupGet(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _chatMessageRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ChatMessage>(), default))
            .ReturnsAsync((ChatMessage message, CancellationToken _) => message);
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .ReturnsAsync(new LlmCompletionResult("Assistant response", 12, false, null));
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));

        _service = new ChatService(
            _unitOfWorkMock.Object,
            _llmProviderMock.Object,
            _plannerMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _notificationServiceMock.Object,
            boardContextBuilder: _boardContextBuilderMock.Object);
    }

    [Fact]
    public async Task SendMessageAsync_IncludesBoardContext_WhenSessionIsBoardScoped()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Board session", boardId);

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        _boardContextBuilderMock
            .Setup(b => b.BuildContextAsync(boardId, default))
            .ReturnsAsync("## Current Board Context\nBoard: My Board\nColumns (in order):\n  - To Do (position 0)\n  - Done (position 1)");

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .Callback<ChatCompletionRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new LlmCompletionResult("Response with context", 20, false, null));

        var result = await _service.SendMessageAsync(session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.BoardContext.Should().NotBeNullOrEmpty();
        capturedRequest.BoardContext.Should().Contain("My Board");
    }

    [Fact]
    public async Task SendMessageAsync_OmitsBoardContext_WhenSessionIsNotBoardScoped()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "General session"); // no boardId

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .Callback<ChatCompletionRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new LlmCompletionResult("Response without context", 15, false, null));

        var result = await _service.SendMessageAsync(session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.BoardContext.Should().BeNull();
        _boardContextBuilderMock.Verify(
            b => b.BuildContextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_HandlesBoardContextBuilderReturningNull()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Board session", boardId);

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        _boardContextBuilderMock
            .Setup(b => b.BuildContextAsync(boardId, default))
            .ReturnsAsync((string?)null);

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .Callback<ChatCompletionRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new LlmCompletionResult("Response", 10, false, null));

        var result = await _service.SendMessageAsync(session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.BoardContext.Should().BeNull();
    }

    [Fact]
    public async Task SendMessageAsync_WorksWithoutBoardContextBuilder()
    {
        // Create service without board context builder (like the existing tests)
        var serviceWithoutBuilder = new ChatService(
            _unitOfWorkMock.Object,
            _llmProviderMock.Object,
            _plannerMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _notificationServiceMock.Object);

        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Board session", boardId);

        _chatSessionRepoMock
            .Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .Callback<ChatCompletionRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new LlmCompletionResult("Response", 10, false, null));

        var result = await serviceWithoutBuilder.SendMessageAsync(session.Id, userId, new SendChatMessageDto("Hello"), default);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.BoardContext.Should().BeNull();
    }
}
