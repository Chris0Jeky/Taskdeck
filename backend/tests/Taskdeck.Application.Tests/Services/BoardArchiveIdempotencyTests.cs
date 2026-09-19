using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class BoardArchiveIdempotencyTests
{
    [Fact]
    public async Task DeleteBoardAsync_ShouldNotWriteOrNotify_WhenBoardIsAlreadyArchived()
    {
        var board = new Board("Already archived");
        board.Archive();
        var (service, unitOfWork, boards, notifier) = CreateService();

        boards.Setup(repository => repository.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        unitOfWork.Setup(work => work.SaveChangesAsync(default))
            .ReturnsAsync(1);

        var result = await service.DeleteBoardAsync(board.Id);

        result.IsSuccess.Should().BeTrue();
        unitOfWork.Verify(work => work.SaveChangesAsync(default), Times.Never);
        boards.Verify(
            repository => repository.GetIsArchivedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notifier.Verify(
            realtime => realtime.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteBoardAsync_ShouldSucceedWithoutDuplicateNotification_WhenConcurrentArchiveAlreadyWon()
    {
        var board = new Board("Concurrent archive");
        var (service, unitOfWork, boards, notifier) = CreateService();

        boards.Setup(repository => repository.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        unitOfWork.Setup(work => work.SaveChangesAsync(default))
            .ThrowsAsync(ConcurrencyConflict());
        boards.Setup(repository => repository.GetIsArchivedAsync(board.Id, default))
            .ReturnsAsync(true);

        var result = await service.DeleteBoardAsync(board.Id);

        result.IsSuccess.Should().BeTrue();
        boards.Verify(repository => repository.GetIsArchivedAsync(board.Id, default), Times.Once);
        notifier.Verify(
            realtime => realtime.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteBoardAsync_ShouldKeepConflict_WhenPersistedBoardIsStillActive()
    {
        var board = new Board("Unresolved conflict");
        var (service, unitOfWork, boards, notifier) = CreateService();

        boards.Setup(repository => repository.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        unitOfWork.Setup(work => work.SaveChangesAsync(default))
            .ThrowsAsync(ConcurrencyConflict());
        boards.Setup(repository => repository.GetIsArchivedAsync(board.Id, default))
            .ReturnsAsync(false);

        var result = await service.DeleteBoardAsync(board.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        boards.Verify(repository => repository.GetIsArchivedAsync(board.Id, default), Times.Once);
        notifier.Verify(
            realtime => realtime.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static (
        BoardService Service,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<IBoardRepository> Boards,
        Mock<IBoardRealtimeNotifier> Notifier) CreateService()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var boards = new Mock<IBoardRepository>();
        var notifier = new Mock<IBoardRealtimeNotifier>();
        unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);

        var service = new BoardService(
            unitOfWork.Object,
            authorizationService: null,
            realtimeNotifier: notifier.Object);

        return (service, unitOfWork, boards, notifier);
    }

    private static DomainException ConcurrencyConflict()
    {
        return new DomainException(
            ErrorCodes.Conflict,
            "Record was updated by another session. Refresh and retry your action.");
    }
}
