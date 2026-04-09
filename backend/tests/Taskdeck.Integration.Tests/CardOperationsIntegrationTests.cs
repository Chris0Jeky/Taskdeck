using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

/// <summary>
/// Integration tests for Card operations against an ephemeral PostgreSQL
/// container. Validates that Card CRUD, column placement, and
/// board-card relationships work correctly with a real relational database.
/// </summary>
[Collection(PostgresTestCollection.Name)]
public class CardOperationsIntegrationTests : PostgresIntegrationTestBase
{
    public CardOperationsIntegrationTests(PostgresContainerFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task CreateCard_InColumn_ShouldPersistWithRelationships()
    {
        var user = new User("cardops-user1", "cardops1@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Card Board", "Board for card tests", user.Id);
        Db.Boards.Add(board);

        var column = new Column(board.Id, "To Do", 0);
        Db.Columns.Add(column);
        await Db.SaveChangesAsync();

        var card = new Card(board.Id, column.Id, "Test Card", "Card description");
        Db.Cards.Add(card);
        await Db.SaveChangesAsync();

        var retrieved = await Db.Cards
            .FirstOrDefaultAsync(c => c.Id == card.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Test Card");
        retrieved.Description.Should().Be("Card description");
        retrieved.BoardId.Should().Be(board.Id);
        retrieved.ColumnId.Should().Be(column.Id);
    }

    [Fact]
    public async Task UpdateCard_ShouldPersistChanges()
    {
        var user = new User("cardops-user2", "cardops2@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Update Card Board", null, user.Id);
        Db.Boards.Add(board);

        var column = new Column(board.Id, "Backlog", 0);
        Db.Columns.Add(column);
        await Db.SaveChangesAsync();

        var card = new Card(board.Id, column.Id, "Original Title", "Original Description");
        Db.Cards.Add(card);
        await Db.SaveChangesAsync();

        card.Update(title: "Updated Title", description: "Updated Description");
        await Db.SaveChangesAsync();

        Db.Entry(card).State = EntityState.Detached;
        var reloaded = await Db.Cards.FindAsync(card.Id);

        reloaded.Should().NotBeNull();
        reloaded!.Title.Should().Be("Updated Title");
        reloaded.Description.Should().Be("Updated Description");
    }

    [Fact]
    public async Task MultipleCards_InSameColumn_ShouldMaintainOrder()
    {
        var user = new User("cardops-user3", "cardops3@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Multi Card Board", null, user.Id);
        Db.Boards.Add(board);

        var column = new Column(board.Id, "In Progress", 0);
        Db.Columns.Add(column);
        await Db.SaveChangesAsync();

        var card1 = new Card(board.Id, column.Id, "First Card", position: 0);
        var card2 = new Card(board.Id, column.Id, "Second Card", position: 1);
        var card3 = new Card(board.Id, column.Id, "Third Card", position: 2);
        Db.Cards.AddRange(card1, card2, card3);
        await Db.SaveChangesAsync();

        var cards = await Db.Cards
            .Where(c => c.ColumnId == column.Id)
            .OrderBy(c => c.Position)
            .ToListAsync();

        cards.Should().HaveCount(3);
        cards[0].Title.Should().Be("First Card");
        cards[1].Title.Should().Be("Second Card");
        cards[2].Title.Should().Be("Third Card");
    }

    [Fact]
    public async Task DeleteCard_ShouldRemoveFromDatabase()
    {
        var user = new User("cardops-user4", "cardops4@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Delete Card Board", null, user.Id);
        Db.Boards.Add(board);

        var column = new Column(board.Id, "Done", 0);
        Db.Columns.Add(column);
        await Db.SaveChangesAsync();

        var card = new Card(board.Id, column.Id, "Card to Delete");
        Db.Cards.Add(card);
        await Db.SaveChangesAsync();

        var cardId = card.Id;

        Db.Cards.Remove(card);
        await Db.SaveChangesAsync();

        var deleted = await Db.Cards.FindAsync(cardId);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task Card_WithDueDate_ShouldPersistTimestamp()
    {
        var user = new User("cardops-user5", "cardops5@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Due Date Board", null, user.Id);
        Db.Boards.Add(board);

        var column = new Column(board.Id, "Scheduled", 0);
        Db.Columns.Add(column);
        await Db.SaveChangesAsync();

        var dueDate = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var card = new Card(board.Id, column.Id, "Due Date Card", dueDate: dueDate);
        Db.Cards.Add(card);
        await Db.SaveChangesAsync();

        Db.Entry(card).State = EntityState.Detached;
        var reloaded = await Db.Cards.FindAsync(card.Id);

        reloaded.Should().NotBeNull();
        reloaded!.DueDate.Should().NotBeNull();
        // Allow small rounding differences from DB storage
        reloaded.DueDate!.Value.Should().BeCloseTo(dueDate, TimeSpan.FromSeconds(1));
    }
}
