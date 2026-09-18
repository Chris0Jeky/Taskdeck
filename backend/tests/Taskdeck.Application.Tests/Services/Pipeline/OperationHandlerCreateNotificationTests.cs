using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public sealed class OperationHandlerCreateNotificationTests
{
    [Fact]
    public async Task ProposalCreate_DiscardedAfterLaterFailure_NeverPublishesRealtime()
    {
        var harness = CreateHarness();
        var deferred = new DeferredBoardRealtimeNotifier(harness.Realtime.Object);

        var result = await harness.Registry.ExecuteOperationAsync(
            harness.Operation,
            CancellationToken.None,
            deferredNotifications: deferred);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        deferred.PendingCount.Should().Be(1, "the create event belongs to the proposal transaction");
        harness.Realtime.Verify(
            notifier => notifier.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        deferred.Discard();

        deferred.PendingCount.Should().Be(0);
        harness.Realtime.Verify(
            notifier => notifier.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposalCreate_SuccessPublishesOnlyWhenTheExecutorFlushesAfterCommit()
    {
        var harness = CreateHarness();
        var deferred = new DeferredBoardRealtimeNotifier(harness.Realtime.Object);

        var result = await harness.Registry.ExecuteOperationAsync(
            harness.Operation,
            CancellationToken.None,
            deferredNotifications: deferred);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        harness.Realtime.Verify(
            notifier => notifier.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        await deferred.FlushAsync();

        harness.Realtime.Verify(
            notifier => notifier.NotifyBoardMutationAsync(
                It.Is<BoardRealtimeEvent>(mutation =>
                    mutation.BoardId == harness.BoardId
                    && mutation.EntityType == "card"
                    && mutation.Operation == "created"
                    && mutation.EntityId == harness.CardId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        deferred.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task NonProposalCreate_StillPublishesImmediately()
    {
        var harness = CreateHarness();

        var result = await harness.Registry.ExecuteOperationAsync(
            harness.Operation,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        harness.Realtime.Verify(
            notifier => notifier.NotifyBoardMutationAsync(
                It.Is<BoardRealtimeEvent>(mutation =>
                    mutation.BoardId == harness.BoardId
                    && mutation.EntityType == "card"
                    && mutation.Operation == "created"
                    && mutation.EntityId == harness.CardId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Harness CreateHarness()
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
        var realtime = new Mock<IBoardRealtimeNotifier>();

        unit.SetupGet(candidate => candidate.Boards).Returns(boards.Object);
        unit.SetupGet(candidate => candidate.Columns).Returns(columns.Object);
        unit.SetupGet(candidate => candidate.Cards).Returns(cards.Object);
        unit.SetupGet(candidate => candidate.Labels).Returns(labels.Object);
        unit.Setup(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        boards.Setup(repository => repository.GetByIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        columns.Setup(repository => repository.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        cards.Setup(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
            .Callback<Card, CancellationToken>((card, _) => created = card)
            .ReturnsAsync((Card card, CancellationToken _) => card);
        cards.Setup(repository => repository.GetByIdWithLabelsAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => created);
        realtime.Setup(notifier => notifier.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cardService = new CardService(unit.Object, realtime.Object);
        var registry = new OperationHandlerRegistry(
            unit.Object,
            cardService,
            new Mock<BoardService>(unit.Object).Object,
            new Mock<ColumnService>(unit.Object).Object);
        var operation = new ProposalOperationDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            "create",
            "card",
            cardId.ToString(),
            JsonSerializer.Serialize(new
            {
                boardId = board.Id,
                columnId = column.Id,
                title = "Created inside proposal"
            }),
            "create-card",
            null);

        return new Harness(registry, realtime, operation, board.Id, cardId);
    }

    private sealed record Harness(
        OperationHandlerRegistry Registry,
        Mock<IBoardRealtimeNotifier> Realtime,
        ProposalOperationDto Operation,
        Guid BoardId,
        Guid CardId);
}
