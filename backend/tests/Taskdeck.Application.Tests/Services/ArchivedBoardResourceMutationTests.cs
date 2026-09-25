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

public class ArchivedBoardResourceMutationTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IBoardRepository> _boards = new();
    private readonly Mock<IColumnRepository> _columns = new();
    private readonly Mock<ILabelRepository> _labels = new();
    private readonly Mock<IBoardRealtimeNotifier> _realtime = new();
    private readonly Mock<IHistoryService> _history = new();
    private readonly Board _board = new("Archive test", ownerId: Guid.NewGuid());
    private readonly Column _column;
    private readonly Column _otherColumn;
    private readonly Label _label;
    private readonly ColumnService _columnService;
    private readonly LabelService _labelService;

    public ArchivedBoardResourceMutationTests()
    {
        _column = new Column(_board.Id, "Before", 0, 3);
        _otherColumn = new Column(_board.Id, "Second", 1, 5);
        _label = new Label(_board.Id, "Before", "#123456");
        _unitOfWork.SetupGet(unit => unit.Boards).Returns(_boards.Object);
        _unitOfWork.SetupGet(unit => unit.Columns).Returns(_columns.Object);
        _unitOfWork.SetupGet(unit => unit.Labels).Returns(_labels.Object);
        _unitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _boards.Setup(repository => repository.GetByIdAsync(_board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_board);
        _columns.Setup(repository => repository.GetByIdAsync(_column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_column);
        _columns.Setup(repository => repository.GetByIdWithCardsAsync(_column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_column);
        _columns.Setup(repository => repository.GetByBoardIdAsync(_board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { _column, _otherColumn });
        _labels.Setup(repository => repository.GetByIdAsync(_label.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_label);
        _labels.Setup(repository => repository.GetByBoardIdAsync(_board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { _label });
        _realtime.Setup(notifier => notifier.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _history.Setup(history => history.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());
        _columnService = new ColumnService(_unitOfWork.Object, _realtime.Object, _history.Object);
        _labelService = new LabelService(_unitOfWork.Object, _realtime.Object, _history.Object);
    }

    [Theory]
    [InlineData("column-create")]
    [InlineData("column-update")]
    [InlineData("column-update-scoped")]
    [InlineData("column-delete")]
    [InlineData("column-delete-scoped")]
    [InlineData("column-reorder")]
    [InlineData("column-reorder-one")]
    [InlineData("label-create")]
    [InlineData("label-update")]
    [InlineData("label-update-scoped")]
    [InlineData("label-delete")]
    [InlineData("label-delete-scoped")]
    public async Task ArchivedBoard_RejectsEveryResourceMutationWithoutSideEffects(string operation)
    {
        _board.Archive();
        var boardStamp = _board.UpdatedAt;
        var columnStamp = _column.UpdatedAt;
        var labelStamp = _label.UpdatedAt;

        var result = await MutateAsync(operation);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("archived board").And.Contain("Restore the board");
        _column.Name.Should().Be("Before");
        _column.Position.Should().Be(0);
        _column.WipLimit.Should().Be(3);
        _otherColumn.Position.Should().Be(1);
        _label.Name.Should().Be("Before");
        _label.ColorHex.Should().Be("#123456");
        _board.UpdatedAt.Should().Be(boardStamp);
        _column.UpdatedAt.Should().Be(columnStamp);
        _label.UpdatedAt.Should().Be(labelStamp);
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _columns.Verify(repository => repository.AddAsync(It.IsAny<Column>(), It.IsAny<CancellationToken>()), Times.Never);
        _columns.Verify(repository => repository.DeleteAsync(It.IsAny<Column>(), It.IsAny<CancellationToken>()), Times.Never);
        _labels.Verify(repository => repository.AddAsync(It.IsAny<Label>(), It.IsAny<CancellationToken>()), Times.Never);
        _labels.Verify(repository => repository.DeleteAsync(It.IsAny<Label>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtime.VerifyNoOtherCalls();
        _history.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("column-create")]
    [InlineData("column-update")]
    [InlineData("column-update-scoped")]
    [InlineData("column-delete")]
    [InlineData("column-delete-scoped")]
    [InlineData("column-reorder")]
    [InlineData("column-reorder-one")]
    [InlineData("label-create")]
    [InlineData("label-update")]
    [InlineData("label-update-scoped")]
    [InlineData("label-delete")]
    [InlineData("label-delete-scoped")]
    public async Task RestoringTheBoard_AllowsTheSameResourceMutation(string operation)
    {
        _board.Archive();
        _board.Unarchive();

        var result = await MutateAsync(operation);

        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _realtime.Verify(notifier => notifier.NotifyBoardMutationAsync(
            It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("column-update-scoped")]
    [InlineData("column-delete-scoped")]
    [InlineData("label-update-scoped")]
    [InlineData("label-delete-scoped")]
    public async Task ForeignBoardResource_RemainsNotFoundBeforeArchiveDisclosure(string operation)
    {
        _board.Archive();
        var result = await MutateAsync(operation, Guid.NewGuid());
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().NotContain("archived");
        _boards.VerifyNoOtherCalls();
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchivedBoard_RemainsReadable()
    {
        _board.Archive();
        var columns = await _columnService.GetColumnsByBoardIdAsync(_board.Id);
        var labels = await _labelService.GetLabelsByBoardIdAsync(_board.Id);
        columns.Value.Select(column => column.Id).Should().Equal(_column.Id, _otherColumn.Id);
        labels.Value.Select(label => label.Id).Should().Equal(_label.Id);
    }

    private async Task<Result> MutateAsync(string operation, Guid? scopedBoardId = null)
    {
        var boardId = scopedBoardId ?? _board.Id;
        return operation switch
        {
            "column-create" => await _columnService.CreateColumnAsync(new CreateColumnDto(_board.Id, "New", 2, null)),
            "column-update" => await _columnService.UpdateColumnAsync(_column.Id, new UpdateColumnDto("After", null, 8)),
            "column-update-scoped" => await _columnService.UpdateColumnAsync(boardId, _column.Id, new UpdateColumnDto("After", null, 8)),
            "column-delete" => await _columnService.DeleteColumnAsync(_column.Id),
            "column-delete-scoped" => await _columnService.DeleteColumnAsync(boardId, _column.Id),
            "column-reorder" => await _columnService.ReorderColumnsAsync(_board.Id, new ReorderColumnsDto(new List<Guid> { _otherColumn.Id, _column.Id })),
            "column-reorder-one" => await _columnService.ReorderColumnAsync(_column.Id, 1),
            "label-create" => await _labelService.CreateLabelAsync(new CreateLabelDto(_board.Id, "New", "#abcdef")),
            "label-update" => await _labelService.UpdateLabelAsync(_label.Id, new UpdateLabelDto("After", "#abcdef")),
            "label-update-scoped" => await _labelService.UpdateLabelAsync(boardId, _label.Id, new UpdateLabelDto("After", "#abcdef")),
            "label-delete" => await _labelService.DeleteLabelAsync(_label.Id),
            "label-delete-scoped" => await _labelService.DeleteLabelAsync(boardId, _label.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown test operation")
        };
    }
}
