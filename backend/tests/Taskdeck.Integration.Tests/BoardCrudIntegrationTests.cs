using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

/// <summary>
/// Integration tests for Board CRUD operations against an ephemeral PostgreSQL
/// container. Validates that Board entity persistence, updates, archiving,
/// and deletion work correctly with a real relational database.
/// </summary>
[Collection(PostgresTestCollection.Name)]
public class BoardCrudIntegrationTests : PostgresIntegrationTestBase
{
    public BoardCrudIntegrationTests(PostgresContainerFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task CreateBoard_ShouldPersistAndRetrieve()
    {
        SkipIfDockerUnavailable();
        var user = new User("boardcrud-user1", "boardcrud1@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Test Board", "A test board for integration", user.Id);
        Db.Boards.Add(board);
        await Db.SaveChangesAsync();

        var retrieved = await Db.Boards.FindAsync(board.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test Board");
        retrieved.Description.Should().Be("A test board for integration");
        retrieved.IsArchived.Should().BeFalse();
        retrieved.OwnerId.Should().Be(user.Id);
    }

    [SkippableFact]
    public async Task UpdateBoard_ShouldPersistChanges()
    {
        SkipIfDockerUnavailable();
        var user = new User("boardcrud-user2", "boardcrud2@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Original Name", "Original Description", user.Id);
        Db.Boards.Add(board);
        await Db.SaveChangesAsync();

        board.Update(name: "Updated Name", description: "Updated Description");
        await Db.SaveChangesAsync();

        // Detach to force re-read from database
        Db.Entry(board).State = EntityState.Detached;
        var reloaded = await Db.Boards.FindAsync(board.Id);

        reloaded.Should().NotBeNull();
        reloaded!.Name.Should().Be("Updated Name");
        reloaded.Description.Should().Be("Updated Description");
    }

    [SkippableFact]
    public async Task ArchiveBoard_ShouldPersistArchivedState()
    {
        SkipIfDockerUnavailable();
        var user = new User("boardcrud-user3", "boardcrud3@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Archive Board", "Will be archived", user.Id);
        Db.Boards.Add(board);
        await Db.SaveChangesAsync();

        board.Archive();
        await Db.SaveChangesAsync();

        Db.Entry(board).State = EntityState.Detached;
        var reloaded = await Db.Boards.FindAsync(board.Id);

        reloaded.Should().NotBeNull();
        reloaded!.IsArchived.Should().BeTrue();
    }

    [SkippableFact]
    public async Task ListBoards_ShouldReturnAllBoards()
    {
        SkipIfDockerUnavailable();
        var user = new User("boardcrud-user4", "boardcrud4@example.com", "hash123");
        Db.Users.Add(user);

        var board1 = new Board("Board A", null, user.Id);
        var board2 = new Board("Board B", "With description", user.Id);
        Db.Boards.AddRange(board1, board2);
        await Db.SaveChangesAsync();

        var boards = await Db.Boards.ToListAsync();

        boards.Should().HaveCountGreaterThanOrEqualTo(2);
        boards.Should().Contain(b => b.Id == board1.Id);
        boards.Should().Contain(b => b.Id == board2.Id);
    }

    [SkippableFact]
    public async Task TransferBoardOwnership_ShouldUpdateOwner()
    {
        SkipIfDockerUnavailable();
        var user1 = new User("boardcrud-user5", "boardcrud5@example.com", "hash123");
        var user2 = new User("boardcrud-user6", "boardcrud6@example.com", "hash123");
        Db.Users.AddRange(user1, user2);

        var board = new Board("Transfer Board", null, user1.Id);
        Db.Boards.Add(board);
        await Db.SaveChangesAsync();

        board.TransferOwnership(user2.Id);
        await Db.SaveChangesAsync();

        Db.Entry(board).State = EntityState.Detached;
        var reloaded = await Db.Boards.FindAsync(board.Id);

        reloaded.Should().NotBeNull();
        reloaded!.OwnerId.Should().Be(user2.Id);
    }
}
