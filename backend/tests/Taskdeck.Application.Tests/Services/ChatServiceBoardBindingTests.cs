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

public sealed class ChatServiceBoardBindingTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IChatSessionRepository> _sessions = new();
    private readonly Mock<IBoardRepository> _boards = new();
    private readonly Mock<IAuthorizationService> _authorization = new();

    public ChatServiceBoardBindingTests()
    {
        _unitOfWork.SetupGet(work => work.ChatSessions).Returns(_sessions.Object);
        _unitOfWork.SetupGet(work => work.Boards).Returns(_boards.Object);
    }

    [Fact]
    public async Task BindBoardAsync_ShouldBindOwnedUnboundSessionAfterWriteAndArchiveChecks()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Unbound");
        var board = new Board("Writable", ownerId: userId);
        _sessions.Setup(repository => repository.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _authorization.Setup(service => service.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boards.Setup(repository => repository.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        _sessions.Setup(repository => repository.TryBindBoardAsync(
                session.Id, userId, boardId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService().BindBoardAsync(
            session.Id,
            userId,
            new BindChatSessionBoardDto(boardId));

        result.IsSuccess.Should().BeTrue();
        result.Value.BoardId.Should().Be(boardId);
        _sessions.Verify(repository => repository.TryBindBoardAsync(
            session.Id, userId, boardId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BindBoardAsync_ShouldReturnSameNotFoundForMissingAndForeignSessions()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var foreign = new ChatSession(ownerId, "Foreign");
        _sessions.Setup(repository => repository.GetByIdWithMessagesAsync(foreign.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreign);
        _sessions.Setup(repository => repository.GetByIdWithMessagesAsync(It.Is<Guid>(id => id != foreign.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        var foreignResult = await CreateService().BindBoardAsync(
            foreign.Id, callerId, new BindChatSessionBoardDto(Guid.NewGuid()));
        var missingResult = await CreateService().BindBoardAsync(
            Guid.NewGuid(), callerId, new BindChatSessionBoardDto(Guid.NewGuid()));

        foreignResult.IsSuccess.Should().BeFalse();
        missingResult.IsSuccess.Should().BeFalse();
        foreignResult.ErrorCode.Should().Be(ErrorCodes.NotFound);
        missingResult.ErrorCode.Should().Be(ErrorCodes.NotFound);
        foreignResult.ErrorMessage.Should().Be(missingResult.ErrorMessage);
        _authorization.Verify(
            service => service.CanWriteBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
            Times.Never);
    }

    [Fact]
    public async Task BindBoardAsync_ShouldRecheckSameBindingAndRejectArchivedOrRevokedBoard()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var session = new ChatSession(userId, "Bound", boardId);
        _sessions.Setup(repository => repository.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _authorization.Setup(service => service.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var revoked = await CreateService().BindBoardAsync(
            session.Id, userId, new BindChatSessionBoardDto(boardId));

        revoked.IsSuccess.Should().BeFalse();
        revoked.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _sessions.Verify(repository => repository.TryBindBoardAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var archivedBoard = new Board("Archived", ownerId: userId);
        archivedBoard.Archive();
        _authorization.Setup(service => service.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boards.Setup(repository => repository.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archivedBoard);

        var archived = await CreateService().BindBoardAsync(
            session.Id, userId, new BindChatSessionBoardDto(boardId));

        archived.IsSuccess.Should().BeFalse();
        archived.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }

    [Fact]
    public async Task BindBoardAsync_ShouldReturnConflictWhenSessionAlreadyUsesDifferentBoard()
    {
        var userId = Guid.NewGuid();
        var session = new ChatSession(userId, "Bound", Guid.NewGuid());
        _sessions.Setup(repository => repository.GetByIdWithMessagesAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await CreateService().BindBoardAsync(
            session.Id, userId, new BindChatSessionBoardDto(Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("new session");
        _authorization.Verify(
            service => service.CanWriteBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
            Times.Never);
    }

    [Fact]
    public async Task BindBoardAsync_ShouldResolveLostCompareAndSetAsIdempotentOrConflict()
    {
        var userId = Guid.NewGuid();
        var requestedBoardId = Guid.NewGuid();
        var otherBoardId = Guid.NewGuid();
        var initiallyUnbound = new ChatSession(userId, "Race");
        var boundToRequested = new ChatSession(userId, "Race", requestedBoardId);
        var boundToOther = new ChatSession(userId, "Race", otherBoardId);
        var board = new Board("Writable", ownerId: userId);
        _authorization.Setup(service => service.CanWriteBoardAsync(userId, requestedBoardId))
            .ReturnsAsync(Result.Success(true));
        _boards.Setup(repository => repository.GetByIdAsync(requestedBoardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        _sessions.Setup(repository => repository.TryBindBoardAsync(
                initiallyUnbound.Id, userId, requestedBoardId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _sessions.SetupSequence(repository => repository.GetByIdWithMessagesAsync(
                initiallyUnbound.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initiallyUnbound)
            .ReturnsAsync(boundToRequested)
            .ReturnsAsync(initiallyUnbound)
            .ReturnsAsync(boundToOther);

        var idempotent = await CreateService().BindBoardAsync(
            initiallyUnbound.Id, userId, new BindChatSessionBoardDto(requestedBoardId));
        var conflict = await CreateService().BindBoardAsync(
            initiallyUnbound.Id, userId, new BindChatSessionBoardDto(requestedBoardId));

        idempotent.IsSuccess.Should().BeTrue();
        idempotent.Value.BoardId.Should().Be(requestedBoardId);
        conflict.IsSuccess.Should().BeFalse();
        conflict.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    private ChatService CreateService() => new(
        _unitOfWork.Object,
        Mock.Of<ILlmProvider>(),
        Mock.Of<IAutomationPlannerService>(),
        Mock.Of<IAutomationProposalService>(),
        Mock.Of<IAutomationPolicyEngine>(),
        authorizationService: _authorization.Object);
}
