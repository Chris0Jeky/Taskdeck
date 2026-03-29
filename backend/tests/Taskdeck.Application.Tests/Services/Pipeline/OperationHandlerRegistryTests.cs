using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public class OperationHandlerRegistryTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<CardService> _cardServiceMock;
    private readonly Mock<BoardService> _boardServiceMock;
    private readonly Mock<ColumnService> _columnServiceMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly OperationHandlerRegistry _registry;

    public OperationHandlerRegistryTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        _cardServiceMock = new Mock<CardService>(_unitOfWorkMock.Object);
        _boardServiceMock = new Mock<BoardService>(_unitOfWorkMock.Object);
        _columnServiceMock = new Mock<ColumnService>(_unitOfWorkMock.Object);

        _registry = new OperationHandlerRegistry(
            _unitOfWorkMock.Object,
            _cardServiceMock.Object,
            _boardServiceMock.Object,
            _columnServiceMock.Object);
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUnsupportedTargetType()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "create", "widget", null,
            """{"title":"Test"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported target type");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUnsupportedCardAction()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "card", null,
            """{"cardId":"some-id"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported card action");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUnsupportedBoardAction()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "board", null,
            """{"boardId":"some-id"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported board action");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUnsupportedColumnAction()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "column", null,
            """{"columnId":"some-id"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported column action");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForEmptyParameters()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null,
            "", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForMissingRequiredCardCreateParameters()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null,
            $$"""{"title":"Test","columnId":"{{Guid.NewGuid()}}"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Missing required parameter 'boardId'");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldSucceed_ForValidBoardUpdate()
    {
        var board = TestDataBuilder.CreateBoard();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", null,
            $$"""{"boardId":"{{board.Id}}","name":"Updated"}""", "key1", null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForColumnReorderWithNegativePosition()
    {
        var columnId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "reorder", "column", null,
            $$"""{"columnId":"{{columnId}}","position":-1}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("must be non-negative");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUpdateCardWithoutTitleOrDescription()
    {
        var cardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "card", null,
            $$"""{"cardId":"{{cardId}}"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("at least one of 'title' or 'description'");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldCatchExceptions_AndReturnFailure()
    {
        var board = TestDataBuilder.CreateBoard();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", null,
            $$"""{"boardId":"{{board.Id}}","name":"Updated"}""", "key1", null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        result.ErrorMessage.Should().Contain("DB error");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldBeCaseInsensitive_ForTargetAndActionTypes()
    {
        var board = TestDataBuilder.CreateBoard();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "Update", "BOARD", null,
            $$"""{"boardId":"{{board.Id}}","name":"Updated"}""", "key1", null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeTrue();
    }
}
