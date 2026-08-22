using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Issue #1960 (+ #1979 for labels) / ADR-0056 section 5: direct human board edits are
/// attributable, so every
/// user-initiated mutation must stamp the acting user on its audit row. These tests pin the
/// actor per mutation class (card create/move/delete; column create/update/delete/reorder;
/// board update/archive/unarchive; label create/update/delete) and pin the lanes that must stay
/// unattributed: the proposal apply
/// pipeline and the CLI/no-actor overloads, whose attribution comes from proposal provenance.
///
/// The actor is always server-side: the services take it from the caller that already
/// authorized the request (controllers pass the claims id from TryGetCurrentUserId), never
/// from a request body field.
/// </summary>
public class DirectCrudAuditActorTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IBoardRepository> _boardRepoMock = new();
    private readonly Mock<IColumnRepository> _columnRepoMock = new();
    private readonly Mock<ICardRepository> _cardRepoMock = new();
    private readonly Mock<ILabelRepository> _labelRepoMock = new();
    private readonly Mock<IHistoryService> _historyServiceMock = new();

    public DirectCrudAuditActorTests()
    {
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Labels).Returns(_labelRepoMock.Object);

        _historyServiceMock
            .Setup(h => h.LogActionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(),
                It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());
    }

    private CardService NewCardService() =>
        new(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

    private ColumnService NewColumnService() =>
        new(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

    private LabelService NewLabelService() =>
        new(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

    private BoardService NewBoardService() =>
        new(_unitOfWorkMock.Object,
            authorizationService: null,
            realtimeNotifier: null,
            historyService: _historyServiceMock.Object);

    private void VerifyActorStamped(string entityType, Guid entityId, AuditAction action, Guid? expectedActor)
    {
        _historyServiceMock.Verify(
            h => h.LogActionAsync(entityType, entityId, action, expectedActor, It.IsAny<string?>()),
            Times.Once);
    }

    #region CardService

    [Fact]
    public async Task CreateCard_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var dto = new CreateCardDto(board.Id, column.Id, "New Card", null, null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default)).ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken _) => c);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid _, CancellationToken __) =>
                TestDataBuilder.CreateCard(board.Id, column.Id, dto.Title));

        var result = await NewCardService().CreateCardAsync(dto, cardId: null, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("card", It.IsAny<Guid>(), AuditAction.Created, ActorId, It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task MoveCard_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var sourceColumn = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var targetColumn = TestDataBuilder.CreateColumn(board.Id, "In Progress", position: 1);
        var card = TestDataBuilder.CreateCard(board.Id, sourceColumn.Id, "Card");
        var dto = new MoveCardDto(targetColumn.Id, 0);

        _cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default)).ReturnsAsync(card);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default)).ReturnsAsync(card);
        _columnRepoMock.Setup(r => r.GetByIdAsync(targetColumn.Id, default)).ReturnsAsync(targetColumn);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(targetColumn.Id, default)).ReturnsAsync(targetColumn);
        _cardRepoMock.Setup(r => r.GetByColumnIdAsync(targetColumn.Id, default)).ReturnsAsync(new List<Card>());

        // Board-scoped overload: the exact call the cards controller makes.
        var result = await NewCardService().MoveCardAsync(board.Id, card.Id, dto, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("card", card.Id, AuditAction.Moved, ActorId);
    }

    [Fact]
    public async Task DeleteCard_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Card");

        _cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default)).ReturnsAsync(card);

        var result = await NewCardService().DeleteCardAsync(board.Id, card.Id, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("card", card.Id, AuditAction.Deleted, ActorId);
    }

    #endregion

    #region ColumnService

    [Fact]
    public async Task CreateColumn_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateColumnDto(board.Id, "New Column", null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default)).ReturnsAsync(new List<Column>());

        var result = await NewColumnService().CreateColumnAsync(dto, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("column", It.IsAny<Guid>(), AuditAction.Created, ActorId, It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateColumn_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var dto = new UpdateColumnDto("Done", null, null);

        _columnRepoMock.Setup(r => r.GetByIdAsync(column.Id, default)).ReturnsAsync(column);

        var result = await NewColumnService().UpdateColumnAsync(board.Id, column.Id, dto, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("column", column.Id, AuditAction.Updated, ActorId);
    }

    [Fact]
    public async Task DeleteColumn_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "Empty Column");

        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default)).ReturnsAsync(column);

        var result = await NewColumnService().DeleteColumnAsync(board.Id, column.Id, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("column", column.Id, AuditAction.Deleted, ActorId);
    }

    [Fact]
    public async Task ReorderColumns_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var first = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var second = TestDataBuilder.CreateColumn(board.Id, "Done", position: 1);
        var dto = new ReorderColumnsDto(new List<Guid> { second.Id, first.Id });

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Column> { first, second });

        var result = await NewColumnService().ReorderColumnsAsync(board.Id, dto, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        // The reorder audit row is keyed on the board, not a single column.
        VerifyActorStamped("column", board.Id, AuditAction.Updated, ActorId);
    }

    #endregion

    #region BoardService

    // BoardMutationAuditTests pins Updated -> null and Unarchived -> null for the *no-actor*
    // overload (UpdateBoardAsync(id, dto)). These two facts pin the actor lane of the same two
    // actions, which is what this PR changed.

    [Fact]
    public async Task UpdateBoard_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var dto = new UpdateBoardDto("Renamed", null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        var result = await NewBoardService().UpdateBoardAsync(board.Id, dto, ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("board", board.Id, AuditAction.Updated, ActorId);
    }

    [Fact]
    public async Task UnarchiveBoard_ViaUpdate_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard(isArchived: true);
        var dto = new UpdateBoardDto(null, null, false);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        var result = await NewBoardService().UpdateBoardAsync(board.Id, dto, ActorId);

        result.IsSuccess.Should().BeTrue();
        // Unarchive audits as Unarchived, not Updated.
        VerifyActorStamped("board", board.Id, AuditAction.Unarchived, ActorId);
    }

    [Fact]
    public async Task ArchiveBoard_ViaUpdate_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var dto = new UpdateBoardDto(null, null, true);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        var result = await NewBoardService().UpdateBoardAsync(board.Id, dto, ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("board", board.Id, AuditAction.Archived, ActorId);
    }

    [Fact]
    public async Task ArchiveBoard_ViaDelete_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        // DELETE /boards/{id} is a soft delete: it archives and audits as Archived.
        var result = await NewBoardService().DeleteBoardAsync(board.Id, ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("board", board.Id, AuditAction.Archived, ActorId);
    }

    #endregion

    #region LabelService

    // Issue #1979. BoardMutationAuditTests pins Created/Updated/Deleted -> null for the *no-actor*
    // overloads; these three facts pin the actor lane of the same three actions. Each test calls
    // the exact overload LabelsController calls.

    [Fact]
    public async Task CreateLabel_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateLabelDto(board.Id, "Urgent", "#FF0000");

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        var result = await NewLabelService().CreateLabelAsync(dto, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("label", It.IsAny<Guid>(), AuditAction.Created, ActorId, It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateLabel_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");
        var dto = new UpdateLabelDto("Feature", "#00FF00");

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default)).ReturnsAsync(label);

        // Board-scoped overload: the exact call the labels controller makes.
        var result = await NewLabelService().UpdateLabelAsync(board.Id, label.Id, dto, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("label", label.Id, AuditAction.Updated, ActorId);
    }

    [Fact]
    public async Task DeleteLabel_StampsActingUserOnAuditRow()
    {
        var board = TestDataBuilder.CreateBoard();
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default)).ReturnsAsync(label);

        var result = await NewLabelService().DeleteLabelAsync(board.Id, label.Id, actorUserId: ActorId);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("label", label.Id, AuditAction.Deleted, ActorId);
    }

    #endregion

    #region Lanes that must stay unattributed

    [Fact]
    public async Task CreateCard_ProposalLaneOverload_LeavesAuditRowUnattributed()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var dto = new CreateCardDto(board.Id, column.Id, "Proposed Card", null, null, null);
        var preAllocatedId = Guid.NewGuid();

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default)).ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken _) => c);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid _, CancellationToken __) =>
                TestDataBuilder.CreateCard(board.Id, column.Id, dto.Title));

        // The apply pipeline's overload: pre-allocated id, no human actor.
        var result = await NewCardService().CreateCardAsync(dto, preAllocatedId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("card", preAllocatedId, AuditAction.Created, null);
    }

    [Fact]
    public async Task ReorderColumn_ProposalLaneOverload_LeavesAuditRowUnattributed()
    {
        var board = TestDataBuilder.CreateBoard();
        var first = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var second = TestDataBuilder.CreateColumn(board.Id, "Done", position: 1);

        _columnRepoMock.Setup(r => r.GetByIdAsync(second.Id, default)).ReturnsAsync(second);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Column> { first, second });

        var result = await NewColumnService().ReorderColumnAsync(second.Id, 0);

        result.IsSuccess.Should().BeTrue();
        VerifyActorStamped("column", second.Id, AuditAction.Updated, null);
    }

    [Fact]
    public async Task CreateLabel_PositionalTokenOverload_LeavesAuditRowUnattributed()
    {
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateLabelDto(board.Id, "Seeded", "#FF0000");

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        // Label CRUD has no proposal-apply lane: OperationHandlerRegistry only resolves existing
        // labels and routes its writes through CardService, and the MCP surface reads labels only.
        // So this positional-token overload is the whole no-actor surface — non-request callers,
        // today just tests. It must not invent an actor.
        var result = await NewLabelService().CreateLabelAsync(dto, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("label", It.IsAny<Guid>(), AuditAction.Created, null, It.IsAny<string?>()),
            Times.Once);
    }

    #endregion

    #region Overload-resolution regressions

    // The actor parameter sits where a Guid already lived on the board-scoped overloads
    // (DeleteCardAsync(boardId, id) / DeleteColumnAsync(boardId, id) / DeleteLabelAsync(boardId, id)).
    // These tests fail
    // loudly if a two-argument call ever rebinds to the single-id + actor overload, which
    // would silently drop the board scoping instead of just losing the actor.
    //
    // Only the delete pairs are exposed: UpdateLabelAsync(boardId, id, dto) cannot rebind to
    // UpdateLabelAsync(id, dto, actorUserId) because the second argument is a Guid and that
    // parameter is an UpdateLabelDto, so no overload guard is owed there.
    //
    // NotFound + "never deleted" alone would NOT discriminate: under the rebinding the service
    // looks up the *board* id, the loose mock returns null, and it fails NotFound without
    // deleting anything either. The load-bearing assertions are the ones that separate the two
    // worlds — the board-scoped error text, and the id the repository was actually queried with.

    [Fact]
    public async Task DeleteCard_TwoArgumentCall_StillScopesToBoard()
    {
        var board = TestDataBuilder.CreateBoard();
        var otherBoardId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(otherBoardId, "To Do");
        var card = TestDataBuilder.CreateCard(otherBoardId, column.Id, "Card");

        _cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default)).ReturnsAsync(card);

        var result = await NewCardService().DeleteCardAsync(board.Id, card.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        // Only the board-scoped overload appends "in board {boardId}"; the single-id overload
        // that a rebinding would select fails with the unscoped "Card with ID {id} not found".
        result.ErrorMessage.Should().Contain($"in board {board.Id}");
        // And the lookup argument is the card — a rebinding would query board.Id instead.
        _cardRepoMock.Verify(r => r.GetByIdAsync(card.Id, default), Times.Once);
        _cardRepoMock.Verify(r => r.GetByIdAsync(board.Id, default), Times.Never);
        _cardRepoMock.Verify(r => r.DeleteAsync(It.IsAny<Card>(), default), Times.Never);
    }

    [Fact]
    public async Task DeleteColumn_TwoArgumentCall_StillScopesToBoard()
    {
        var board = TestDataBuilder.CreateBoard();
        var otherBoardId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(otherBoardId, "To Do");

        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default)).ReturnsAsync(column);

        var result = await NewColumnService().DeleteColumnAsync(board.Id, column.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        // Only the board-scoped overload appends "in board {boardId}"; the single-id overload
        // that a rebinding would select fails with the unscoped "Column with ID {id} not found".
        result.ErrorMessage.Should().Contain($"in board {board.Id}");
        // And the lookup argument is the column — a rebinding would query board.Id instead.
        _columnRepoMock.Verify(r => r.GetByIdWithCardsAsync(column.Id, default), Times.Once);
        _columnRepoMock.Verify(r => r.GetByIdWithCardsAsync(board.Id, default), Times.Never);
        _columnRepoMock.Verify(r => r.DeleteAsync(It.IsAny<Column>(), default), Times.Never);
    }

    [Fact]
    public async Task DeleteLabel_TwoArgumentCall_StillScopesToBoard()
    {
        var board = TestDataBuilder.CreateBoard();
        var otherBoardId = Guid.NewGuid();
        var label = TestDataBuilder.CreateLabel(otherBoardId, "Bug", "#FF0000");

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default)).ReturnsAsync(label);

        var result = await NewLabelService().DeleteLabelAsync(board.Id, label.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        // Only the board-scoped overload appends "in board {boardId}"; the single-id overload
        // that a rebinding would select fails with the unscoped "Label with ID {id} not found".
        result.ErrorMessage.Should().Contain($"in board {board.Id}");
        // And the lookup argument is the label — a rebinding would query board.Id instead.
        _labelRepoMock.Verify(r => r.GetByIdAsync(label.Id, default), Times.Once);
        _labelRepoMock.Verify(r => r.GetByIdAsync(board.Id, default), Times.Never);
        _labelRepoMock.Verify(r => r.DeleteAsync(It.IsAny<Label>(), default), Times.Never);
    }

    #endregion
}
