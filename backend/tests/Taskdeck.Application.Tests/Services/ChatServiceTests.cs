using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ChatServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IChatSessionRepository> _chatSessionRepoMock;
    private readonly Mock<IChatMessageRepository> _chatMessageRepoMock;
    private readonly Mock<ILlmProvider> _llmProviderMock;
    private readonly ChatService _service;

    public ChatServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _chatSessionRepoMock = new Mock<IChatSessionRepository>();
        _chatMessageRepoMock = new Mock<IChatMessageRepository>();
        _llmProviderMock = new Mock<ILlmProvider>();

        _unitOfWorkMock.Setup(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);

        _service = new ChatService(_unitOfWorkMock.Object, _llmProviderMock.Object);
    }

    #region CreateSessionAsync Tests

    [Fact]
    public async Task CreateSessionAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new CreateChatSessionDto("Test Session");

        _chatSessionRepoMock.Setup(r => r.AddAsync(It.IsAny<ChatSession>(), default))
            .ReturnsAsync((ChatSession s, CancellationToken ct) => s);

        // Act
        var result = await _service.CreateSessionAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Test Session");
        result.Value.UserId.Should().Be(userId);
        result.Value.Status.Should().Be(ChatSessionStatus.Active);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldReturnFailure_WithEmptyTitle()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new CreateChatSessionDto("");

        // Act
        var result = await _service.CreateSessionAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region GetSessionAsync Tests

    [Fact]
    public async Task GetSessionAsync_ShouldReturnSession_WhenExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "My Session");

        _chatSessionRepoMock.Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);

        // Act
        var result = await _service.GetSessionAsync(session.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("My Session");
        result.Value.Id.Should().Be(session.Id);
    }

    [Fact]
    public async Task GetSessionAsync_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _chatSessionRepoMock.Setup(r => r.GetByIdWithMessagesAsync(sessionId, default))
            .ReturnsAsync((ChatSession?)null);

        // Act
        var result = await _service.GetSessionAsync(sessionId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region GetUserSessionsAsync Tests

    [Fact]
    public async Task GetUserSessionsAsync_ShouldReturnSessions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessions = new List<ChatSession>
        {
            new ChatSession(userId, "Session 1"),
            new ChatSession(userId, "Session 2")
        };

        _chatSessionRepoMock.Setup(r => r.GetByUserIdAsync(userId, 100, default))
            .ReturnsAsync(sessions);

        // Act
        var result = await _service.GetUserSessionsAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    #endregion

    #region SendMessageAsync Tests

    [Fact]
    public async Task SendMessageAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Test Session");
        var dto = new SendChatMessageDto("Hello, world!");

        _chatSessionRepoMock.Setup(r => r.GetByIdWithMessagesAsync(session.Id, default))
            .ReturnsAsync(session);
        _chatMessageRepoMock.Setup(r => r.AddAsync(It.IsAny<ChatMessage>(), default))
            .ReturnsAsync((ChatMessage m, CancellationToken ct) => m);
        _llmProviderMock.Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), default))
            .ReturnsAsync(new LlmCompletionResult("I can help with that.", TokensUsed: 10, IsActionable: false));

        // Act
        var result = await _service.SendMessageAsync(session.Id, userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be(ChatMessageRole.Assistant);
        result.Value.Content.Should().Be("I can help with that.");
        result.Value.MessageType.Should().Be("text");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldReturnNotFound_WhenSessionNotExists()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dto = new SendChatMessageDto("Hello");

        _chatSessionRepoMock.Setup(r => r.GetByIdWithMessagesAsync(sessionId, default))
            .ReturnsAsync((ChatSession?)null);

        // Act
        var result = await _service.SendMessageAsync(sessionId, userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion
}
