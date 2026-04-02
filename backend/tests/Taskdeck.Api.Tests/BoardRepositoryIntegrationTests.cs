using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for BoardRepository against real SQLite.
/// Covers includeArchived filtering, deep include chain (GetByIdWithDetailsAsync),
/// readable-query access via ownership and BoardAccess, and search behavior.
/// </summary>
public class BoardRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BoardRepositoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetReadableByUserIdAsync_ExcludeArchived_ShouldOmitArchivedBoards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-arch-user", "brd-arch@example.com", "hash");
        db.Users.Add(user);

        var active = new Board("Active board for filter", ownerId: user.Id);
        var archived = new Board("Archived board for filter", ownerId: user.Id);
        archived.Archive();
        db.Boards.AddRange(active, archived);
        await db.SaveChangesAsync();

        var excludeArchived = (await repo.GetReadableByUserIdAsync(user.Id, includeArchived: false)).ToList();
        var includeArchived = (await repo.GetReadableByUserIdAsync(user.Id, includeArchived: true)).ToList();

        excludeArchived.Should().Contain(b => b.Id == active.Id);
        excludeArchived.Should().NotContain(b => b.Id == archived.Id);

        includeArchived.Should().Contain(b => b.Id == active.Id);
        includeArchived.Should().Contain(b => b.Id == archived.Id);
    }

    [Fact]
    public async Task GetReadableByUserIdAsync_ShouldIncludeBoardsSharedViaBoardAccess()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var owner = new User("brd-owner", "brd-owner@example.com", "hash");
        var collaborator = new User("brd-collab", "brd-collab@example.com", "hash");
        db.Users.AddRange(owner, collaborator);

        var ownedBoard = new Board("Owner only board", ownerId: owner.Id);
        var sharedBoard = new Board("Shared board", ownerId: owner.Id);
        db.Boards.AddRange(ownedBoard, sharedBoard);
        await db.SaveChangesAsync();

        // Grant collaborator access to sharedBoard
        var access = new BoardAccess(sharedBoard.Id, collaborator.Id, UserRole.Editor, owner.Id);
        db.BoardAccesses.Add(access);
        await db.SaveChangesAsync();

        var collabBoards = (await repo.GetReadableByUserIdAsync(collaborator.Id, includeArchived: false)).ToList();

        collabBoards.Should().Contain(b => b.Id == sharedBoard.Id);
        collabBoards.Should().NotContain(b => b.Id == ownedBoard.Id);
    }

    [Fact]
    public async Task CountReadableByUserIdAsync_ShouldCountOwnedAndSharedBoards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-cnt-user", "brd-cnt@example.com", "hash");
        var otherOwner = new User("brd-cnt-other", "brd-cnt-other@example.com", "hash");
        db.Users.AddRange(user, otherOwner);

        var owned = new Board("Count owned", ownerId: user.Id);
        var shared = new Board("Count shared", ownerId: otherOwner.Id);
        var unrelated = new Board("Count unrelated", ownerId: otherOwner.Id);
        db.Boards.AddRange(owned, shared, unrelated);
        await db.SaveChangesAsync();

        db.BoardAccesses.Add(new BoardAccess(shared.Id, user.Id, UserRole.Viewer, otherOwner.Id));
        await db.SaveChangesAsync();

        var count = await repo.CountReadableByUserIdAsync(user.Id, includeArchived: false);
        count.Should().BeGreaterOrEqualTo(2); // owned + shared
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_ShouldLoadDeepIncludeChain()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-detail-user", "brd-detail@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Details board", ownerId: user.Id);
        db.Boards.Add(board);

        var column = new Column(board.Id, "Todo", 0);
        db.Columns.Add(column);

        var label = new Label(board.Id, "Bug", "#FF0000");
        db.Labels.Add(label);

        var card = new Card(board.Id, column.Id, "Test card");
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        // Add card-label relationship
        var cardLabel = new CardLabel(card.Id, label.Id);
        db.CardLabels.Add(cardLabel);
        await db.SaveChangesAsync();

        var result = await repo.GetByIdWithDetailsAsync(board.Id);

        result.Should().NotBeNull();
        result!.Columns.Should().HaveCountGreaterOrEqualTo(1);
        result.Labels.Should().HaveCountGreaterOrEqualTo(1);

        var loadedColumn = result.Columns.First(c => c.Id == column.Id);
        loadedColumn.Cards.Should().HaveCountGreaterOrEqualTo(1);

        var loadedCard = loadedColumn.Cards.First(c => c.Id == card.Id);
        loadedCard.CardLabels.Should().HaveCountGreaterOrEqualTo(1);
        loadedCard.CardLabels.First().Label.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRecentReadableByUserIdAsync_ShouldRespectLimit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-recent-user", "brd-recent@example.com", "hash");
        db.Users.Add(user);

        for (var i = 0; i < 5; i++)
        {
            db.Boards.Add(new Board($"Recent board {i}", ownerId: user.Id));
        }
        await db.SaveChangesAsync();

        var results = (await repo.GetRecentReadableByUserIdAsync(user.Id, limit: 2, includeArchived: false)).ToList();
        results.Count.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task GetRecentReadableByUserIdAsync_WithZeroLimit_ShouldReturnEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-zero-user", "brd-zero@example.com", "hash");
        db.Users.Add(user);
        db.Boards.Add(new Board("Zero limit board", ownerId: user.Id));
        await db.SaveChangesAsync();

        var results = (await repo.GetRecentReadableByUserIdAsync(user.Id, limit: 0, includeArchived: false)).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ShouldMatchNameAndDescription()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-search-user", "brd-search@example.com", "hash");
        db.Users.Add(user);

        var nameMatch = new Board("UniqueSearchToken board", ownerId: user.Id);
        var descMatch = new Board("Other board", "Contains UniqueSearchToken in desc", ownerId: user.Id);
        var noMatch = new Board("No match board", "Nothing here", ownerId: user.Id);
        db.Boards.AddRange(nameMatch, descMatch, noMatch);
        await db.SaveChangesAsync();

        var results = (await repo.SearchAsync("UniqueSearchToken", includeArchived: false)).ToList();

        results.Should().Contain(b => b.Id == nameMatch.Id);
        results.Should().Contain(b => b.Id == descMatch.Id);
        results.Should().NotContain(b => b.Id == noMatch.Id);
    }

    [Fact]
    public async Task SearchAsync_WithNullSearchText_ShouldReturnAll()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-null-user", "brd-null@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Null search board", ownerId: user.Id);
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        var results = (await repo.SearchAsync(null, includeArchived: false)).ToList();

        // Should return boards (at least the one we created)
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetOwnedBoardIdsAsync_ShouldReturnOnlyOwnedSubset()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var userA = new User("brd-own-a", "brd-own-a@example.com", "hash");
        var userB = new User("brd-own-b", "brd-own-b@example.com", "hash");
        db.Users.AddRange(userA, userB);

        var ownedByA = new Board("Owned by A", ownerId: userA.Id);
        var ownedByB = new Board("Owned by B", ownerId: userB.Id);
        db.Boards.AddRange(ownedByA, ownedByB);
        await db.SaveChangesAsync();

        var result = (await repo.GetOwnedBoardIdsAsync(userA.Id, new[] { ownedByA.Id, ownedByB.Id })).ToList();

        result.Should().Contain(ownedByA.Id);
        result.Should().NotContain(ownedByB.Id);
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_ForNonexistentId_ShouldReturnNull()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var result = await repo.GetByIdWithDetailsAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task CountReadableUpdatedSinceAsync_ShouldFilterByUpdatedAt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-upd-user", "brd-upd@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Updated board", ownerId: user.Id);
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        // Count since before creation should include it
        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        var count = await repo.CountReadableUpdatedSinceAsync(user.Id, since, includeArchived: false);
        count.Should().BeGreaterOrEqualTo(1);

        // Count since far future should not include it
        var future = DateTimeOffset.UtcNow.AddDays(1);
        var futureCount = await repo.CountReadableUpdatedSinceAsync(user.Id, future, includeArchived: false);
        futureCount.Should().Be(0);
    }
}
