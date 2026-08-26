using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// `#2115`. The per-user board list is cached for <see cref="CacheSettings.BoardListTtlSeconds"/> and
/// CardService has no cache awareness, so anything a card write moves on the Board row is served
/// stale until that TTL expires. The archive-race guard therefore advances a non-user-visible marker
/// (<see cref="Board.CardMutationMarker"/>) instead of re-stamping <c>UpdatedAt</c>. These tests pin
/// the resulting property: a card write leaves the cached list agreeing with the board it was built
/// from, so no invalidation is owed.
/// </summary>
public class BoardListCacheCoherenceTests
{
    // A fixed instant — the drift assertions must not be able to pass by accident because the test
    // happened to run inside one clock tick.
    private static readonly DateTimeOffset SeededUpdatedAt = new(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);

    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IBoardRepository> _boardRepoMock = new();
    private readonly Mock<IColumnRepository> _columnRepoMock = new();
    private readonly Mock<ICardRepository> _cardRepoMock = new();
    private readonly DictionaryCacheService _cache = new();
    private readonly Guid _userId = Guid.NewGuid();

    public BoardListCacheCoherenceTests()
    {
        _unitOfWorkMock.SetupGet(work => work.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.SetupGet(work => work.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.SetupGet(work => work.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task CreateCard_LeavesTheCachedBoardListAgreeingWithTheBoard()
    {
        var (board, column) = SeedBoard();
        var boardService = CreateBoardService();
        var cardService = new CardService(_unitOfWorkMock.Object);

        // Populate the cache from the pre-write board.
        (await boardService.ListBoardsAsync(_userId)).Value.Should().ContainSingle();
        _cache.Entries.Should().ContainKey(CacheKeys.BoardListForUser(_userId));

        var created = await cardService.CreateCardAsync(
            new CreateCardDto(board.Id, column.Id, "Card added after the list was cached", null, null, null));
        created.IsSuccess.Should().BeTrue();

        // Still a cache hit — a card write does not, and need not, invalidate the entry.
        _boardRepoMock.Invocations.Clear();
        var servedFromCache = (await boardService.ListBoardsAsync(_userId)).Value.ToList();
        _boardRepoMock.Verify(
            repository => repository.SearchIdsAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the second list must be the cached one, or this test proves nothing about staleness");

        // What a reader with a cold cache gets for the very same board, after the write.
        _cache.Entries.Clear();
        var freshFromRepository = (await boardService.ListBoardsAsync(_userId)).Value.ToList();

        servedFromCache.Should().BeEquivalentTo(freshFromRepository,
            "a card write must not move any field the cached board list carries");
    }

    [Fact]
    public async Task CreateCard_DoesNotMoveTheBoardTimestampTheCachedListServes()
    {
        var (board, column) = SeedBoard();
        var boardService = CreateBoardService();
        var cardService = new CardService(_unitOfWorkMock.Object);

        var cachedTimestamp = (await boardService.ListBoardsAsync(_userId)).Value.Single().UpdatedAt;
        cachedTimestamp.Should().Be(SeededUpdatedAt);

        var created = await cardService.CreateCardAsync(
            new CreateCardDto(board.Id, column.Id, "Card", null, null, null));
        created.IsSuccess.Should().BeTrue();

        board.UpdatedAt.Should().Be(cachedTimestamp,
            "the cached list serves Board.UpdatedAt, and the TTL would hide any drift for BoardListTtlSeconds");
        board.CardMutationMarker.Should().Be(1,
            "the write still has to move the guard marker so EF joins the concurrency-token predicate");
    }

    [Fact]
    public async Task UpdateAndDeleteCard_DoNotMoveTheBoardTimestampTheCachedListServes()
    {
        var (board, column) = SeedBoard();
        var card = new Card(board.Id, column.Id, "Existing", null, null, 0);
        _cardRepoMock
            .Setup(repository => repository.GetByIdWithLabelsAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        _cardRepoMock
            .Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        _cardRepoMock
            .Setup(repository => repository.DeleteAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cardService = new CardService(_unitOfWorkMock.Object);

        (await cardService.UpdateCardAsync(card.Id, new UpdateCardDto("Renamed", null, null, null, null, null)))
            .IsSuccess.Should().BeTrue();
        board.UpdatedAt.Should().Be(SeededUpdatedAt);

        (await cardService.DeleteCardAsync(card.Id)).IsSuccess.Should().BeTrue();
        board.UpdatedAt.Should().Be(SeededUpdatedAt);

        board.CardMutationMarker.Should().Be(2, "both writes still move the guard marker");
    }

    private BoardService CreateBoardService() => new(
        _unitOfWorkMock.Object,
        authorizationService: null,
        realtimeNotifier: null,
        historyService: null,
        cacheService: _cache,
        cacheSettings: new CacheSettings { BoardListTtlSeconds = 60 });

    /// <summary>
    /// One board and one column, with the board's <c>UpdatedAt</c> pinned to a fixed instant. Every
    /// repository read hands back the same instance, which is what EF change tracking does inside one
    /// unit of work — so the list and the card write see one board, exactly as in production.
    /// </summary>
    private (Board Board, Column Column) SeedBoard()
    {
        var board = new Board("Cached board", "desc", _userId);
        typeof(Board).GetProperty(nameof(Board.UpdatedAt))!.SetValue(board, SeededUpdatedAt);

        var column = new Column(board.Id, "To Do", 0);

        _boardRepoMock
            .Setup(repository => repository.SearchIdsAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { board.Id });
        _boardRepoMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Board> { board });
        _boardRepoMock
            .Setup(repository => repository.GetByIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        _columnRepoMock
            .Setup(repository => repository.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        _cardRepoMock
            .Setup(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
            .Returns((Card card, CancellationToken _) =>
            {
                // CreateCardAsync re-reads the card it just added to build its DTO.
                _cardRepoMock
                    .Setup(repository => repository.GetByIdWithLabelsAsync(card.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(card);
                return Task.FromResult(card);
            });

        return (board, column);
    }

    /// <summary>
    /// A real cache rather than a verify-only mock: staleness is observable only when a value written
    /// before the mutation is read back after it.
    /// </summary>
    private sealed class DictionaryCacheService : ICacheService
    {
        public Dictionary<string, object> Entries { get; } = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
            => Task.FromResult(Entries.TryGetValue(key, out var value) ? (T?)value : null);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class
        {
            Entries[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Entries.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
        {
            foreach (var key in Entries.Keys.Where(key => key.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                Entries.Remove(key);

            return Task.CompletedTask;
        }
    }
}
