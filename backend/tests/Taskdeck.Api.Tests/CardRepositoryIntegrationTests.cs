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

        var col1 = new Column(board.Id, "First", 0);
        var col2 = new Column(board.Id, "Second", 1);
        db.Columns.AddRange(col1, col2);

        var cardA = new Card(board.Id, col1.Id, "A in first col", position: 1);
        var cardB = new Card(board.Id, col1.Id, "B in first col", position: 0);
        var cardC = new Card(board.Id, col2.Id, "C in second col", position: 0);
        db.Cards.AddRange(cardA, cardB, cardC);
        await db.SaveChangesAsync();

        var results = (await repo.GetByBoardIdAsync(board.Id)).ToList();

        // Column ordering: col1 cards before col2 cards
        var idxB = results.FindIndex(c => c.Id == cardB.Id); // pos 0 in col1
        var idxA = results.FindIndex(c => c.Id == cardA.Id); // pos 1 in col1
        var idxC = results.FindIndex(c => c.Id == cardC.Id); // pos 0 in col2

        idxB.Should().BeLessThan(idxA); // lower position first within same column
        idxA.Should().BeLessThan(idxC); // col1 cards before col2 cards
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
}
