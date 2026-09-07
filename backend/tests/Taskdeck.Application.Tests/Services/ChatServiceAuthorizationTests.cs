using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class ChatServiceAuthorizationTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IChatSessionRepository> _chatSessionRepositoryMock = new();
    private readonly Mock<IChatMessageRepository> _chatMessageRepositoryMock = new();
    private readonly Mock<ILlmProvider> _llmProviderMock = new();
    private readonly Mock<IAutomationPlannerService> _plannerMock = new();
    private readonly Mock<IAutomationProposalService> _proposalServiceMock = new();
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();
    private readonly Mock<IBoardContextBuilder> _boardContextBuilderMock = new();

    public ChatServiceAuthorizationTests()
    {
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.ChatSessions)
            .Returns(_chatSessionRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.ChatMessages)
            .Returns(_chatMessageRepositoryMock.Object);
        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _chatMessageRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessage message, CancellationToken _) => message);
        _llmProviderMock
            .Setup(provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult("Assistant response", 12, false, null));
        _notificationServiceMock
            .Setup(service => service.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldDenyUnreadableBoardBeforePersistence()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await CreateService().CreateSessionAsync(
            userId,
            new CreateChatSessionDto("Foreign board chat", boardId));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Be("Board not found");
        _chatSessionRepositoryMock.Verify(
            repository => repository.AddAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldNormalizeMissingBoardAuthorizationFailure()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.NotFound, $"Board with ID {boardId} not found"));

        var result = await CreateService().CreateSessionAsync(
            userId,
            new CreateChatSessionDto("Board chat", boardId));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Be("Board not found");
        _chatSessionRepositoryMock.Verify(
            repository => repository.AddAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldPreserveNonNotFoundAuthorizationFailure()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.UnexpectedError, "authorization service unavailable"));

        var result = await CreateService().CreateSessionAsync(
            userId,
            new CreateChatSessionDto("Board chat", boardId));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        result.ErrorMessage.Should().Be("authorization service unavailable");
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldFailClosedWhenAuthorizationServiceIsMissing()
    {
        var result = await CreateService(includeAuthorization: false).CreateSessionAsync(
            Guid.NewGuid(),
            new CreateChatSessionDto("Board chat", Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Be("You do not have access to this board");
        _chatSessionRepositoryMock.Verify(
            repository => repository.AddAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldPersistReadableBoardSession()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _chatSessionRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession session, CancellationToken _) => session);

        var result = await CreateService().CreateSessionAsync(
            userId,
            new CreateChatSessionDto("Readable board chat", boardId));

        result.IsSuccess.Should().BeTrue();
        result.Value.BoardId.Should().Be(boardId);
        _chatSessionRepositoryMock.Verify(
            repository => repository.AddAsync(
                It.Is<ChatSession>(session => session.UserId == userId && session.BoardId == boardId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldDenyUnreadableBoardBeforeMessageAndProviderWork()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Revoked board chat", boardId);
        _chatSessionRepositoryMock
            .Setup(repository => repository.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));
        var quotaMock = new Mock<ILlmQuotaService>();

        var result = await CreateService(quotaService: quotaMock.Object).SendMessageAsync(
            session.Id,
            userId,
            new SendChatMessageDto("create card 'Should not run'"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Be("Board not found");
        session.Messages.Should().BeEmpty();
        _chatMessageRepositoryMock.Verify(
            repository => repository.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _boardContextBuilderMock.Verify(
            builder => builder.BuildContextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _llmProviderMock.Verify(
            provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _plannerMock.Verify(
            planner => planner.ParseInstructionAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<ProposalSourceType>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
        quotaMock.Verify(
            quota => quota.ReserveAsync(userId, LlmSurface.Chat, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldPreserveReadableBoardPath()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Readable board chat", boardId);
        _chatSessionRepositoryMock
            .Setup(repository => repository.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boardContextBuilderMock
            .Setup(builder => builder.BuildContextAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("board context");

        ChatCompletionRequest? capturedRequest = null;
        _llmProviderMock
            .Setup(provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatCompletionRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new LlmCompletionResult("Response", 12, false, null));

        var result = await CreateService().SendMessageAsync(
            session.Id,
            userId,
            new SendChatMessageDto("Hello"));

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.BoardContext.Should().Be("board context");
        _llmProviderMock.Verify(
            provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _boardContextBuilderMock.Verify(
            builder => builder.BuildContextAsync(boardId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StreamResponseAsync_ShouldYieldBoardAccessErrorBeforeProviderWork()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Revoked board stream", boardId);
        _chatSessionRepositoryMock
            .Setup(repository => repository.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));
        var quotaMock = new Mock<ILlmQuotaService>();

        var events = new List<LlmTokenEvent>();
        await foreach (var tokenEvent in CreateService(quotaService: quotaMock.Object)
            .StreamResponseAsync(session.Id, userId))
        {
            events.Add(tokenEvent);
        }

        events.Should().ContainSingle();
        events[0].IsComplete.Should().BeTrue();
        events[0].Token.Should().BeEmpty();
        events[0].Error.Should().Be("Board not found");
        _boardContextBuilderMock.Verify(
            builder => builder.BuildContextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _llmProviderMock.Verify(
            provider => provider.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        quotaMock.Verify(
            quota => quota.ReserveAsync(userId, LlmSurface.Chat, It.IsAny<CancellationToken>()),
            Times.Never);
        _chatMessageRepositoryMock.Verify(
            repository => repository.AddAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private ChatService CreateService(
        ILlmQuotaService? quotaService = null,
        bool includeAuthorization = true) => new(
        _unitOfWorkMock.Object,
        _llmProviderMock.Object,
        _plannerMock.Object,
        _proposalServiceMock.Object,
        _policyEngineMock.Object,
        _notificationServiceMock.Object,
        authorizationService: includeAuthorization ? _authorizationServiceMock.Object : null,
        quotaService: quotaService,
        boardContextBuilder: _boardContextBuilderMock.Object);
}
