using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for CardRepository against real SQLite.
/// Covers pagination boundary, multi-board queries, search across boards,
/// label filtering, and ordering correctness.
/// </summary>
public class CardRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CardRepositoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetByBoardIdAsync_ShouldReturnOnlyCardsForBoard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-board-user", "card-board@example.com", "hash");
        db.Users.Add(user);

        var boardA = new Board("Card board A", ownerId: user.Id);
        var boardB = new Board("Card board B", ownerId: user.Id);
        db.Boards.AddRange(boardA, boardB);

        var colA = new Column(boardA.Id, "Todo", 0);
        var colB = new Column(boardB.Id, "Todo", 0);
        db.Columns.AddRange(colA, colB);

        var cardA = new Card(boardA.Id, colA.Id, "Card on A");
        var cardB = new Card(boardB.Id, colB.Id, "Card on B");
        db.Cards.AddRange(cardA, cardB);
        await db.SaveChangesAsync();

        var results = (await repo.GetByBoardIdAsync(boardA.Id)).ToList();

        results.Should().Contain(c => c.Id == cardA.Id);
        results.Should().NotContain(c => c.Id == cardB.Id);
    }

    [Fact]
    public async Task GetByBoardIdsAsync_ShouldReturnCardsFromMultipleBoards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-multi-user", "card-multi@example.com", "hash");
        db.Users.Add(user);

        var boardA = new Board("Multi board A", ownerId: user.Id);
        var boardB = new Board("Multi board B", ownerId: user.Id);
        var boardC = new Board("Multi board C", ownerId: user.Id);
        db.Boards.AddRange(boardA, boardB, boardC);

        var colA = new Column(boardA.Id, "Todo", 0);
        var colB = new Column(boardB.Id, "Todo", 0);
        var colC = new Column(boardC.Id, "Todo", 0);
        db.Columns.AddRange(colA, colB, colC);

        var cardA = new Card(boardA.Id, colA.Id, "Card A");
        var cardB = new Card(boardB.Id, colB.Id, "Card B");
        var cardC = new Card(boardC.Id, colC.Id, "Card C");
        db.Cards.AddRange(cardA, cardB, cardC);
        await db.SaveChangesAsync();

        var results = (await repo.GetByBoardIdsAsync(new[] { boardA.Id, boardB.Id })).ToList();

        results.Should().Contain(c => c.Id == cardA.Id);
        results.Should().Contain(c => c.Id == cardB.Id);
        results.Should().NotContain(c => c.Id == cardC.Id);
    }

    [Fact]
    public async Task GetByBoardIdsAsync_WithEmptyList_ShouldReturnEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var results = (await repo.GetByBoardIdsAsync(Array.Empty<Guid>())).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAcrossBoardsAsync_ShouldPaginateCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-page-user", "card-page@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Pagination board", ownerId: user.Id);
        db.Boards.Add(board);

        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        // Create cards with matching search text
        for (var i = 0; i < 5; i++)
        {
            db.Cards.Add(new Card(board.Id, col.Id, $"PaginateToken card {i}", position: i));
        }
        await db.SaveChangesAsync();

        // Page 1: offset 0, take 2
        var page1 = (await repo.SearchAcrossBoardsAsync(
            new[] { board.Id }, "PaginateToken", maxResults: 2, offset: 0)).ToList();
        page1.Count.Should().Be(2);

        // Page 2: offset 2, take 2
        var page2 = (await repo.SearchAcrossBoardsAsync(
            new[] { board.Id }, "PaginateToken", maxResults: 2, offset: 2)).ToList();
        page2.Count.Should().Be(2);

        // No overlap between pages
        page1.Select(c => c.Id).Should().NotIntersectWith(page2.Select(c => c.Id));
    }

    [Fact]
    public async Task SearchAcrossBoardsAsync_WithOffsetBeyondTotal_ShouldReturnEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-beyond-user", "card-beyond@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Beyond board", ownerId: user.Id);
        db.Boards.Add(board);

        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        db.Cards.Add(new Card(board.Id, col.Id, "BeyondToken single card"));
        await db.SaveChangesAsync();

        // Offset far beyond total count
        var results = (await repo.SearchAcrossBoardsAsync(
            new[] { board.Id }, "BeyondToken", maxResults: 10, offset: 100)).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CountSearchAcrossBoardsAsync_ShouldMatchResults()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-count-user", "card-count@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Count board", ownerId: user.Id);
        db.Boards.Add(board);

        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        db.Cards.Add(new Card(board.Id, col.Id, "CountToken alpha"));
        db.Cards.Add(new Card(board.Id, col.Id, "CountToken beta"));
        db.Cards.Add(new Card(board.Id, col.Id, "No match card"));
        await db.SaveChangesAsync();

        var count = await repo.CountSearchAcrossBoardsAsync(new[] { board.Id }, "CountToken");
        count.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_WithLabelFilter_ShouldReturnOnlyLabeledCards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-label-user", "card-label@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Label filter board", ownerId: user.Id);
        db.Boards.Add(board);

        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        var label = new Label(board.Id, "Urgent", "#FF0000");
        db.Labels.Add(label);

        var labeled = new Card(board.Id, col.Id, "Labeled card");
        var unlabeled = new Card(board.Id, col.Id, "Unlabeled card");
        db.Cards.AddRange(labeled, unlabeled);
        await db.SaveChangesAsync();

        db.CardLabels.Add(new CardLabel(labeled.Id, label.Id));
        await db.SaveChangesAsync();

        var results = (await repo.SearchAsync(board.Id, searchText: null, labelId: label.Id, columnId: null)).ToList();

        results.Should().Contain(c => c.Id == labeled.Id);
        results.Should().NotContain(c => c.Id == unlabeled.Id);
    }

    [Fact]
    public async Task SearchAsync_WithColumnFilter_ShouldReturnOnlyColumnCards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-colf-user", "card-colf@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Column filter board", ownerId: user.Id);
        db.Boards.Add(board);

        var colTodo = new Column(board.Id, "Todo", 0);
        var colDone = new Column(board.Id, "Done", 1);
        db.Columns.AddRange(colTodo, colDone);

        var inTodo = new Card(board.Id, colTodo.Id, "In todo");
        var inDone = new Card(board.Id, colDone.Id, "In done");
        db.Cards.AddRange(inTodo, inDone);
        await db.SaveChangesAsync();

        var results = (await repo.SearchAsync(board.Id, searchText: null, labelId: null, columnId: colTodo.Id)).ToList();

        results.Should().Contain(c => c.Id == inTodo.Id);
        results.Should().NotContain(c => c.Id == inDone.Id);
    }

    [Fact]
    public async Task GetByBoardIdAsync_ShouldOrderByColumnThenPosition()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-order-user", "card-order@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Order board", ownerId: user.Id);
        db.Boards.Add(board);

        // Use a single column to test position ordering (avoids GUID-based column ordering)
        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        var cardPos2 = new Card(board.Id, col.Id, "Position 2", position: 2);
        var cardPos0 = new Card(board.Id, col.Id, "Position 0", position: 0);
        var cardPos1 = new Card(board.Id, col.Id, "Position 1", position: 1);
        db.Cards.AddRange(cardPos2, cardPos0, cardPos1);
        await db.SaveChangesAsync();

        var results = (await repo.GetByBoardIdAsync(board.Id)).ToList();

        var idx0 = results.FindIndex(c => c.Id == cardPos0.Id);
        var idx1 = results.FindIndex(c => c.Id == cardPos1.Id);
        var idx2 = results.FindIndex(c => c.Id == cardPos2.Id);

        // Cards should be ordered by Position within the same column
        idx0.Should().BeLessThan(idx1);
        idx1.Should().BeLessThan(idx2);
    }

    /// <summary>
    /// Regression for #1133: <see cref="ICardRepository.GetByBoardIdAsync"/> uses
    /// <c>AsSplitQuery()</c> to avoid a cartesian fan-out across the CardLabels-&gt;Label
    /// collection. The split query must not change results: every card appears exactly once,
    /// with all of its labels loaded, ordered by position within the column.
    /// </summary>
    [Fact]
    public async Task GetByBoardIdAsync_WithMultipleLabels_LoadsAllLabelsWithoutDuplicatingCards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-split-user", "card-split@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Split query board", ownerId: user.Id);
        db.Boards.Add(board);

        // Single column so ordering is by Position (avoids GUID-based ColumnId ordering).
        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        var labelX = new Label(board.Id, "Urgent", "#FF0000");
        var labelY = new Label(board.Id, "Backend", "#00FF00");
        db.Labels.AddRange(labelX, labelY);

        // card0 has TWO labels (the case a cartesian single query would fan out),
        // card1 has ONE label, card2 has NONE.
        var card0 = new Card(board.Id, col.Id, "Card 0", position: 0);
        var card1 = new Card(board.Id, col.Id, "Card 1", position: 1);
        var card2 = new Card(board.Id, col.Id, "Card 2", position: 2);
        db.Cards.AddRange(card0, card1, card2);
        await db.SaveChangesAsync();

        db.CardLabels.AddRange(
            new CardLabel(card0.Id, labelX.Id),
            new CardLabel(card0.Id, labelY.Id),
            new CardLabel(card1.Id, labelX.Id));
        await db.SaveChangesAsync();

        var results = (await repo.GetByBoardIdAsync(board.Id)).ToList();

        // Split query must not duplicate or drop root rows.
        results.Should().HaveCount(3);
        results.Select(c => c.Id).Should().OnlyHaveUniqueItems();

        // Ordered by Position within the column.
        results[0].Id.Should().Be(card0.Id);
        results[1].Id.Should().Be(card1.Id);
        results[2].Id.Should().Be(card2.Id);

        // All labels are loaded for each card.
        results[0].CardLabels.Select(cl => cl.Label.Name)
            .Should().BeEquivalentTo(new[] { "Urgent", "Backend" });
        results[1].CardLabels.Select(cl => cl.Label.Name)
            .Should().BeEquivalentTo(new[] { "Urgent" });
        results[2].CardLabels.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAgendaByBoardIdsAsync_ShouldReturnOnlyBlockedOrDueDateCards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-agenda-user", "card-agenda@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Agenda board", ownerId: user.Id);
        db.Boards.Add(board);

        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        var blocked = new Card(board.Id, col.Id, "Blocked card");
        blocked.Block("dependency");
        var withDueDate = new Card(board.Id, col.Id, "Due date card", dueDate: DateTimeOffset.UtcNow.AddDays(1));
        var normal = new Card(board.Id, col.Id, "Normal card");
        db.Cards.AddRange(blocked, withDueDate, normal);
        await db.SaveChangesAsync();

        var results = (await repo.GetAgendaByBoardIdsAsync(new[] { board.Id })).ToList();

        results.Should().Contain(c => c.Id == blocked.Id);
        results.Should().Contain(c => c.Id == withDueDate.Id);
        results.Should().NotContain(c => c.Id == normal.Id);
    }

    [Fact]
    public async Task GetByIdWithLabelsAsync_ShouldLoadLabels()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-lbls-user", "card-lbls@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Labels board", ownerId: user.Id);
        db.Boards.Add(board);

        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        var label = new Label(board.Id, "Feature", "#00FF00");
        db.Labels.Add(label);

        var card = new Card(board.Id, col.Id, "With labels");
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        db.CardLabels.Add(new CardLabel(card.Id, label.Id));
        await db.SaveChangesAsync();

        var result = await repo.GetByIdWithLabelsAsync(card.Id);

        result.Should().NotBeNull();
        result!.CardLabels.Should().HaveCount(1);
        result.CardLabels.First().Label.Should().NotBeNull();
        result.CardLabels.First().Label.Name.Should().Be("Feature");
    }

    [Fact]
    public async Task SearchAcrossBoardsAsync_WithEmptyBoardIds_ShouldReturnEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var results = (await repo.SearchAcrossBoardsAsync(
            Array.Empty<Guid>(), "anything", maxResults: 10)).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAcrossBoardsAsync_WithEmptySearchText_ShouldReturnEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-empty-user", "card-empty@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Empty search board", ownerId: user.Id);
        db.Boards.Add(board);

        var col = new Column(board.Id, "Todo", 0);
        db.Columns.Add(col);

        db.Cards.Add(new Card(board.Id, col.Id, "Some card"));
        await db.SaveChangesAsync();

        var results = (await repo.SearchAcrossBoardsAsync(
            new[] { board.Id }, "", maxResults: 10)).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTitleMatchesByBoardIdAsync_ShouldPreserveUnicodeMatchingScopeOrderAndBounds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("card-title-match-user", "card-title-match@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Title match board", ownerId: user.Id);
        var otherBoard = new Board("Other title match board", ownerId: user.Id);
        db.Boards.AddRange(board, otherBoard);
        var column = new Column(board.Id, "Todo", 0);
        var otherColumn = new Column(otherBoard.Id, "Todo", 0);
        db.Columns.AddRange(column, otherColumn);

        var first = new Card(board.Id, column.Id, "Needle first", position: 0);
        var second = new Card(board.Id, column.Id, "nEeDlE second", position: 1);
        var literalPercent = new Card(board.Id, column.Id, "Literal % marker", position: 2);
        var unicode = new Card(board.Id, column.Id, "Café planning", position: 3);
        var other = new Card(otherBoard.Id, otherColumn.Id, "Needle other board", position: 0);
        db.Cards.AddRange(first, second, literalPercent, unicode, other);
        await db.SaveChangesAsync();

        var limitedMatches = await repo.GetTitleMatchesByBoardIdAsync(
            board.Id, "NEEDLE", maxResults: 1, maxCardsToScan: 10);
        var literalMatches = await repo.GetTitleMatchesByBoardIdAsync(
            board.Id, "%", maxResults: 10, maxCardsToScan: 10);
        var unicodeMatches = await repo.GetTitleMatchesByBoardIdAsync(
            board.Id, "CAFÉ", maxResults: 10, maxCardsToScan: 10);
        var truncatedScan = await repo.GetTitleMatchesByBoardIdAsync(
            board.Id, "absent", maxResults: 2, maxCardsToScan: 2);

        limitedMatches.CardIds.Should().Equal(first.Id);
        literalMatches.CardIds.Should().Equal(literalPercent.Id);
        unicodeMatches.CardIds.Should().Equal(unicode.Id);
        truncatedScan.CardIds.Should().BeEmpty();
        truncatedScan.IsExhaustive.Should().BeFalse();
    }

    [Fact]
    public async Task GetTitleMatchesByBoardIdAsync_ShouldHonorCancellation()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICardRepository>();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var act = () => repo.GetTitleMatchesByBoardIdAsync(
            Guid.NewGuid(),
            "cancelled",
            maxResults: 1,
            maxCardsToScan: 1,
            cancellationToken: cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
