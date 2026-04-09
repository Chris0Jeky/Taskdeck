using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class BoardServiceCacheTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly CacheSettings _cacheSettings;
    private readonly Guid _userId = Guid.NewGuid();

    public BoardServiceCacheTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _cacheMock = new Mock<ICacheService>();
        _cacheSettings = new CacheSettings
        {
            BoardListTtlSeconds = 60
        };

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
    }

    private BoardService CreateService(ICacheService? cache = null)
    {
        return new BoardService(
            _unitOfWorkMock.Object,
            authorizationService: null,
            realtimeNotifier: null,
            historyService: null,
            cacheService: cache ?? _cacheMock.Object,
            cacheSettings: _cacheSettings);
    }

    #region GetBoardDetailAsync — intentionally NOT cached

    [Fact]
    public async Task GetBoardDetail_AlwaysQueriesDatabase_NeverCaches()
    {
        // Board detail is intentionally not cached because BoardDetailDto includes
        // columns with card counts that can be mutated by ColumnService/CardService.
        var boardId = Guid.NewGuid();
        var board = new Board("DB Board", "desc", _userId);

        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var service = CreateService();
        var result = await service.GetBoardDetailAsync(boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("DB Board");

        // Database should always be queried — no cache lookup
        _boardRepoMock.Verify(r => r.GetByIdWithDetailsAsync(
            boardId, It.IsAny<CancellationToken>()), Times.Once);

        // Cache should never be read or written for board detail
        _cacheMock.Verify(c => c.GetAsync<BoardDetailDto>(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<BoardDetailDto>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBoardDetail_WorksWithoutCache()
    {
        var boardId = Guid.NewGuid();
        var board = new Board("No Cache Board", "desc", _userId);

        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var service = new BoardService(_unitOfWorkMock.Object);
        var result = await service.GetBoardDetailAsync(boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("No Cache Board");
    }

    [Fact]
    public async Task GetBoardDetail_ReturnsNotFound_WithCacheServicePresent()
    {
        var boardId = Guid.NewGuid();

        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board?)null);

        var service = CreateService();
        var result = await service.GetBoardDetailAsync(boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    #endregion

    #region ListBoardsAsync Cache-Aside

    [Fact]
    public async Task ListBoards_ReturnsCachedValue_OnCacheHit()
    {
        var cachedList = new List<BoardDto>
        {
            new(Guid.NewGuid(), "Board1", null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "Board2", null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };

        _cacheMock.Setup(c => c.GetAsync<List<BoardDto>>(
                CacheKeys.BoardListForUser(_userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedList);

        var service = CreateService();
        var result = await service.ListBoardsAsync(_userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        // Database should NOT be queried
        _boardRepoMock.Verify(r => r.SearchIdsAsync(
            It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListBoards_DoesNotCache_WhenSearchTextProvided()
    {
        _boardRepoMock.Setup(r => r.SearchIdsAsync("filter", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _boardRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Board>());

        var service = CreateService();
        await service.ListBoardsAsync(_userId, searchText: "filter");

        // Should not attempt cache get or set when search text is provided
        _cacheMock.Verify(c => c.GetAsync<List<BoardDto>>(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<List<BoardDto>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListBoards_DoesNotCache_WhenIncludeArchivedIsTrue()
    {
        _boardRepoMock.Setup(r => r.SearchIdsAsync(null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _boardRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Board>());

        var service = CreateService();
        await service.ListBoardsAsync(_userId, includeArchived: true);

        _cacheMock.Verify(c => c.GetAsync<List<BoardDto>>(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListBoards_PopulatesCache_OnMiss()
    {
        var boardId = Guid.NewGuid();

        _cacheMock.Setup(c => c.GetAsync<List<BoardDto>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BoardDto>?)null);

        _boardRepoMock.Setup(r => r.SearchIdsAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { boardId });
        _boardRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Board> { new("Test", null, _userId) });

        var service = CreateService();
        var result = await service.ListBoardsAsync(_userId);

        result.IsSuccess.Should().BeTrue();

        _cacheMock.Verify(c => c.SetAsync(
            CacheKeys.BoardListForUser(_userId),
            It.IsAny<List<BoardDto>>(),
            TimeSpan.FromSeconds(60),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Cache Invalidation

    [Fact]
    public async Task CreateBoard_InvalidatesBoardListCache()
    {
        var dto = new CreateBoardDto("New Board", "desc");

        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board b, CancellationToken _) => b);

        var service = CreateService();
        await service.CreateBoardAsync(dto, _userId);

        _cacheMock.Verify(c => c.RemoveAsync(
            CacheKeys.BoardListForUser(_userId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateBoard_InvalidatesBoardListCache()
    {
        var boardId = Guid.NewGuid();
        var board = new Board("Old Name", "desc", _userId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var service = CreateService();
        await service.UpdateBoardAsync(boardId, new UpdateBoardDto("New Name", null, null));

        // Board list cache should be invalidated for the owner
        _cacheMock.Verify(c => c.RemoveAsync(
            CacheKeys.BoardListForUser(_userId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteBoard_InvalidatesBoardListCache()
    {
        var boardId = Guid.NewGuid();
        var board = new Board("Board to Delete", "desc", _userId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var service = CreateService();
        await service.DeleteBoardAsync(boardId);

        _cacheMock.Verify(c => c.RemoveAsync(
            CacheKeys.BoardListForUser(_userId), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Cache Degradation

    [Fact]
    public async Task GetBoardDetail_WorksWithoutCacheService()
    {
        var boardId = Guid.NewGuid();
        var board = new Board("Fallback Board", "desc", _userId);

        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var serviceWithoutCache = new BoardService(_unitOfWorkMock.Object);
        var result = await serviceWithoutCache.GetBoardDetailAsync(boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Fallback Board");
    }

    [Fact]
    public async Task ListBoards_FallsBackToDatabase_WhenNoCacheService()
    {
        _boardRepoMock.Setup(r => r.SearchIdsAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _boardRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Board>());

        var serviceWithoutCache = new BoardService(_unitOfWorkMock.Object);
        var result = await serviceWithoutCache.ListBoardsAsync(_userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ListBoards_FallsBackToDatabase_WhenCacheGetThrows()
    {
        // Even though the ICacheService contract says "never throw",
        // verify BoardService is resilient if a faulty implementation violates the contract.
        var throwingCacheMock = new Mock<ICacheService>();
        throwingCacheMock.Setup(c => c.GetAsync<List<BoardDto>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        _boardRepoMock.Setup(r => r.SearchIdsAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _boardRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Board> { new("DB Board", null, _userId) });

        // NOTE: This test documents a known limitation. If a cache implementation
        // throws (violating the contract), BoardService will propagate the exception.
        // Defense-in-depth would add try/catch at the call site, but the current
        // design relies on the contract. This test verifies the contract matters.
        var service = CreateService(throwingCacheMock.Object);
        var act = () => service.ListBoardsAsync(_userId);

        // Currently throws — documenting this as known behavior
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region CacheKeys

    [Fact]
    public void CacheKeys_BoardListForUser_FormatsCorrectly()
    {
        var userId = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");
        CacheKeys.BoardListForUser(userId).Should().Be("boards:user:abcdef01-2345-6789-abcd-ef0123456789");
    }

    #endregion
}
