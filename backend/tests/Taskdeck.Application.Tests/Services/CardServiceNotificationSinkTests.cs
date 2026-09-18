using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class CardServiceNotificationSinkTests
{
    [Fact]
    public async Task ExplicitCreateSinkOwnsNotificationUntilOuterTransactionFlushes()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "Next");
        var cardId = Guid.NewGuid();
        Card? created = null;

        var unit = new Mock<IUnitOfWork>();
        var boards = new Mock<IBoardRepository>();
        var columns = new Mock<IColumnRepository>();
        var cards = new Mock<ICardRepository>();
        var labels = new Mock<ILabelRepository>();
        var constructorNotifier = new Mock<IBoardRealtimeNotifier>();
        var committedNotifier = new Mock<IBoardRealtimeNotifier>();

        unit.SetupGet(candidate => candidate.Boards).Returns(boards.Object);
        unit.SetupGet(candidate => candidate.Columns).Returns(columns.Object);
        unit.SetupGet(candidate => candidate.Cards).Returns(cards.Object);
        unit.SetupGet(candidate => candidate.Labels).Returns(labels.Object);
        unit.Setup(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        boards.Setup(repository => repository.GetByIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        columns.Setup(repository => repository.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        cards.Setup(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
            .Callback<Card, CancellationToken>((card, _) => created = card)
            .ReturnsAsync((Card card, CancellationToken _) => card);
        cards.Setup(repository => repository.GetByIdWithLabelsAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => created);
        committedNotifier.Setup(notifier => notifier.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var deferred = new DeferredBoardRealtimeNotifier(committedNotifier.Object);
        var service = new CardService(unit.Object, constructorNotifier.Object);

        var result = await service.CreateCardAsync(
            new CreateCardDto(board.Id, column.Id, "Created inside proposal", null, null, null),
            cardId,
            cancellationToken: CancellationToken.None,
            notificationSink: deferred);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        deferred.PendingCount.Should().Be(1);
        constructorNotifier.Verify(notifier => notifier.NotifyBoardMutationAsync(
            It.IsAny<BoardRealtimeEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
        committedNotifier.Verify(notifier => notifier.NotifyBoardMutationAsync(
            It.IsAny<BoardRealtimeEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await deferred.FlushAsync();

        committedNotifier.Verify(notifier => notifier.NotifyBoardMutationAsync(
            It.Is<BoardRealtimeEvent>(mutation =>
                mutation.BoardId == board.Id
                && mutation.EntityType == "card"
                && mutation.Operation == "created"
                && mutation.EntityId == cardId),
            It.IsAny<CancellationToken>()), Times.Once);
        constructorNotifier.Verify(notifier => notifier.NotifyBoardMutationAsync(
            It.IsAny<BoardRealtimeEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
