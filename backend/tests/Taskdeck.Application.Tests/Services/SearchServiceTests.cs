using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class SearchServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly SearchService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public SearchServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _cardRepoMock = new Mock<ICardRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);

        _service = new SearchService(_unitOfWorkMock.Object);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyWithPaginationMetadata()
    {
        var result = await _service.SearchAsync(_userId, "");

        result.IsSuccess.Should().BeTrue();
        result.Value.Boards.Should().BeEmpty();
        result.Value.Cards.Should().BeEmpty();
        result.Value.TotalCardCount.Should().Be(0);
        result.Value.HasMoreCards.Should().BeFalse();
        result.Value.Offset.Should().Be(0);
        result.Value.MaxResults.Should().Be(20);
    }

    [Fact]
    public async Task SearchAsync_ShortQuery_ReturnsEmptyWithPaginationMetadata()
    {
        var result = await _service.SearchAsync(_userId, "a");

        result.IsSuccess.Should().BeTrue();
        result.Value.Cards.Should().BeEmpty();
        result.Value.TotalCardCount.Should().Be(0);
        result.Value.HasMoreCards.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_EmptyUserId_ReturnsFailure()
    {
        var result = await _service.SearchAsync(Guid.Empty, "test");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_DefaultParams_UsesMaxResults20AndOffset0()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        SetupReadableBoards(board);
        SetupCardSearch(new List<Card>(), totalCount: 0);

        var result = await _service.SearchAsync(_userId, "test");

        result.IsSuccess.Should().BeTrue();
        result.Value.MaxResults.Should().Be(20);
        result.Value.Offset.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_CustomMaxResults_IsRespected()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        SetupReadableBoards(board);
        SetupCardSearch(new List<Card>(), totalCount: 0);

        var result = await _service.SearchAsync(_userId, "test", maxResults: 5);

        result.IsSuccess.Should().BeTrue();
        result.Value.MaxResults.Should().Be(5);

        _cardRepoMock.Verify(r => r.SearchAcrossBoardsAsync(
            It.IsAny<IEnumerable<Guid>>(),
            "test",
            5,
            0,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MaxResultsExceedsCeiling_ClampedTo100()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        SetupReadableBoards(board);
        SetupCardSearch(new List<Card>(), totalCount: 0);

        var result = await _service.SearchAsync(_userId, "test", maxResults: 500);

        result.IsSuccess.Should().BeTrue();
        result.Value.MaxResults.Should().Be(100);

        _cardRepoMock.Verify(r => r.SearchAcrossBoardsAsync(
            It.IsAny<IEnumerable<Guid>>(),
            "test",
            100,
            0,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MaxResultsZero_ClampedTo1()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        SetupReadableBoards(board);
        SetupCardSearch(new List<Card>(), totalCount: 0);

        var result = await _service.SearchAsync(_userId, "test", maxResults: 0);

        result.IsSuccess.Should().BeTrue();
        result.Value.MaxResults.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_NegativeOffset_ClampedToZero()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        SetupReadableBoards(board);
        SetupCardSearch(new List<Card>(), totalCount: 0);

        var result = await _service.SearchAsync(_userId, "test", offset: -5);

        result.IsSuccess.Should().BeTrue();
        result.Value.Offset.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_OffsetBeyondResults_ReturnsEmptyCardsWithCorrectTotal()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        SetupReadableBoards(board);
        SetupCardSearch(new List<Card>(), totalCount: 5);

        var result = await _service.SearchAsync(_userId, "test", maxResults: 20, offset: 100);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cards.Should().BeEmpty();
        result.Value.TotalCardCount.Should().Be(5);
        result.Value.HasMoreCards.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_HasMoreCards_ReturnsTrueWhenMoreExist()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        var column = TestDataBuilder.CreateColumn(board.Id);
        var cards = Enumerable.Range(0, 5)
            .Select(i => TestDataBuilder.CreateCard(board.Id, column.Id, $"Card {i}"))
            .ToList();

        SetupReadableBoards(board);
        SetupCardSearch(cards, totalCount: 25);

        var result = await _service.SearchAsync(_userId, "Card", maxResults: 5, offset: 0);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cards.Should().HaveCount(5);
        result.Value.TotalCardCount.Should().Be(25);
        result.Value.HasMoreCards.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_HasMoreCards_ReturnsFalseWhenAllReturned()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        var column = TestDataBuilder.CreateColumn(board.Id);
        var cards = Enumerable.Range(0, 3)
            .Select(i => TestDataBuilder.CreateCard(board.Id, column.Id, $"Card {i}"))
            .ToList();

        SetupReadableBoards(board);
        SetupCardSearch(cards, totalCount: 3);

        var result = await _service.SearchAsync(_userId, "Card", maxResults: 20, offset: 0);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cards.Should().HaveCount(3);
        result.Value.TotalCardCount.Should().Be(3);
        result.Value.HasMoreCards.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_OffsetPagination_PassesCorrectOffset()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        SetupReadableBoards(board);
        SetupCardSearch(new List<Card>(), totalCount: 50);

        var result = await _service.SearchAsync(_userId, "test", maxResults: 10, offset: 20);

        result.IsSuccess.Should().BeTrue();
        result.Value.Offset.Should().Be(20);

        _cardRepoMock.Verify(r => r.SearchAcrossBoardsAsync(
            It.IsAny<IEnumerable<Guid>>(),
            "test",
            10,
            20,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_CountIsCalledForTotalCardCount()
    {
        var board = TestDataBuilder.CreateBoard("Test Board");
        SetupReadableBoards(board);
        SetupCardSearch(new List<Card>(), totalCount: 42);

        var result = await _service.SearchAsync(_userId, "test");

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCardCount.Should().Be(42);

        _cardRepoMock.Verify(r => r.CountSearchAcrossBoardsAsync(
            It.IsAny<IEnumerable<Guid>>(),
            "test",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Helpers

    private void SetupReadableBoards(params Board[] boards)
    {
        _boardRepoMock
            .Setup(r => r.GetReadableByUserIdAsync(_userId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(boards.ToList());
    }

    private void SetupCardSearch(List<Card> cards, int totalCount)
    {
        _cardRepoMock
            .Setup(r => r.SearchAcrossBoardsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cards);

        _cardRepoMock
            .Setup(r => r.CountSearchAcrossBoardsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(totalCount);
    }

    #endregion
}
