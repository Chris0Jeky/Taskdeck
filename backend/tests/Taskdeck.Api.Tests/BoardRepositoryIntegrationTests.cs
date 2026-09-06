using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
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
        count.Should().Be(2); // owned + shared
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
        result!.Columns.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Labels.Should().HaveCountGreaterThanOrEqualTo(1);

        var loadedColumn = result.Columns.First(c => c.Id == column.Id);
        loadedColumn.Cards.Should().HaveCountGreaterThanOrEqualTo(1);

        var loadedCard = loadedColumn.Cards.First(c => c.Id == card.Id);
        loadedCard.CardLabels.Should().HaveCountGreaterThanOrEqualTo(1);
        loadedCard.CardLabels.First().Label.Should().NotBeNull();
    }

    /// <summary>
    /// Regression for #1133: the graph-correctness assertion alone cannot distinguish the
    /// split-query implementation from a single cartesian fan-out query. Capture the commands
    /// for only this read and prove that the real SQLite provider executed multiple SELECTs.
    /// </summary>
    [Fact]
    public async Task GetByIdWithDetailsAsync_WithMultipleCollections_ExecutesSplitSelects()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-board-split-{Guid.NewGuid():N}.db");
        var interceptor = new CapturingCommandInterceptor();
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .AddInterceptors(interceptor)
                .Options;

            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var user = new User("brd-split-user", "brd-split@example.com", "hash");
            var board = new Board("Split-query board", ownerId: user.Id);
            var firstColumn = new Column(board.Id, "Todo", 0);
            var secondColumn = new Column(board.Id, "Done", 1);
            var firstLabel = new Label(board.Id, "Bug", "#FF0000");
            var secondLabel = new Label(board.Id, "Feature", "#00FF00");
            db.Users.Add(user);
            db.Boards.Add(board);
            db.Columns.AddRange(firstColumn, secondColumn);
            db.Labels.AddRange(firstLabel, secondLabel);
            await db.SaveChangesAsync();

            var firstCard = new Card(board.Id, firstColumn.Id, "Fix the query");
            var secondCard = new Card(board.Id, secondColumn.Id, "Ship the proof");
            db.Cards.AddRange(firstCard, secondCard);
            await db.SaveChangesAsync();

            db.CardLabels.AddRange(
                new CardLabel(firstCard.Id, firstLabel.Id),
                new CardLabel(secondCard.Id, secondLabel.Id));
            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            interceptor.Clear();

            var result = await new BoardRepository(db).GetByIdWithDetailsAsync(board.Id);

            result.Should().NotBeNull();
            result!.Columns.Should().HaveCount(2);
            result.Columns.SelectMany(column => column.Cards).Should().HaveCount(2);
            result.Labels.Should().HaveCount(2);
            result.Columns
                .SelectMany(column => column.Cards)
                .SelectMany(card => card.CardLabels)
                .Should()
                .HaveCount(2)
                .And.OnlyContain(cardLabel => cardLabel.Label != null);

            var selectCommands = interceptor.Snapshot()
                .Where(sql => sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                .ToList();
            selectCommands.Should().HaveCountGreaterThan(
                1,
                "the board's sibling collections must be loaded by multiple real SQLite SELECTs");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".migrate.lock" })
            {
                var path = dbPath + suffix;
                if (!File.Exists(path))
                {
                    continue;
                }

                try { File.Delete(path); }
                catch (IOException) { /* best-effort test cleanup */ }
            }
        }
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
        results.Count.Should().BeLessThanOrEqualTo(2);
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
        count.Should().Be(1);

        // Count since far future should not include it
        var future = DateTimeOffset.UtcNow.AddDays(1);
        var futureCount = await repo.CountReadableUpdatedSinceAsync(user.Id, future, includeArchived: false);
        futureCount.Should().Be(0);
    }

    [Fact]
    public async Task CountCollaborationMembersAsync_ShouldReturnZero_WhenUserHasNoBoards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("brd-collab-none", "brd-collab-none@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var count = await repo.CountCollaborationMembersAsync(user.Id);

        count.Should().Be(0);
    }

    [Fact]
    public async Task CountCollaborationMembersAsync_ShouldCountOwner_WhenNoAccessRowsExist()
    {
        // Owners deliberately hold no BoardAccess row (AuthorizationService short-circuits on
        // OwnerId), so a count built from access rows alone would report zero for a solo owner.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var owner = new User("brd-collab-solo", "brd-collab-solo@example.com", "hash");
        db.Users.Add(owner);
        db.Boards.Add(new Board("Solo owner board", ownerId: owner.Id));
        await db.SaveChangesAsync();

        var count = await repo.CountCollaborationMembersAsync(owner.Id);

        count.Should().Be(1);
    }

    [Fact]
    public async Task CountCollaborationMembersAsync_ShouldFlipToTwo_WhenASecondMemberIsGranted()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var owner = new User("brd-collab-owner", "brd-collab-owner@example.com", "hash");
        var collaborator = new User("brd-collab-guest", "brd-collab-guest@example.com", "hash");
        db.Users.AddRange(owner, collaborator);

        var board = new Board("Shared collaboration board", ownerId: owner.Id);
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        (await repo.CountCollaborationMembersAsync(owner.Id)).Should().Be(1);

        db.BoardAccesses.Add(new BoardAccess(board.Id, collaborator.Id, UserRole.Editor, owner.Id));
        await db.SaveChangesAsync();

        (await repo.CountCollaborationMembersAsync(owner.Id)).Should().Be(2);
        (await repo.CountCollaborationMembersAsync(collaborator.Id)).Should().Be(2);
    }

    [Fact]
    public async Task CountCollaborationMembersAsync_ShouldIgnoreBoardsTheUserCannotRead()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var isolated = new User("brd-collab-isolated", "brd-collab-isolated@example.com", "hash");
        var stranger = new User("brd-collab-stranger", "brd-collab-stranger@example.com", "hash");
        var strangerGuest = new User("brd-collab-strangerguest", "brd-collab-strangerguest@example.com", "hash");
        db.Users.AddRange(isolated, stranger, strangerGuest);

        db.Boards.Add(new Board("Isolated own board", ownerId: isolated.Id));
        var strangerBoard = new Board("Stranger shared board", ownerId: stranger.Id);
        db.Boards.Add(strangerBoard);
        await db.SaveChangesAsync();

        db.BoardAccesses.Add(new BoardAccess(strangerBoard.Id, strangerGuest.Id, UserRole.Editor, stranger.Id));
        await db.SaveChangesAsync();

        (await repo.CountCollaborationMembersAsync(isolated.Id)).Should().Be(1);
    }

    [Fact]
    public async Task CountCollaborationMembersAsync_ShouldNotDoubleCountAMemberSharedAcrossBoards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var owner = new User("brd-collab-multi", "brd-collab-multi@example.com", "hash");
        var collaborator = new User("brd-collab-multiguest", "brd-collab-multiguest@example.com", "hash");
        db.Users.AddRange(owner, collaborator);

        var first = new Board("Multi board one", ownerId: owner.Id);
        var second = new Board("Multi board two", ownerId: owner.Id);
        db.Boards.AddRange(first, second);
        await db.SaveChangesAsync();

        db.BoardAccesses.AddRange(
            new BoardAccess(first.Id, collaborator.Id, UserRole.Editor, owner.Id),
            new BoardAccess(second.Id, collaborator.Id, UserRole.Viewer, owner.Id));
        await db.SaveChangesAsync();

        (await repo.CountCollaborationMembersAsync(owner.Id)).Should().Be(2);
    }

    [Fact]
    public async Task CountCollaborationMembersAsync_ShouldIncludeArchivedBoardMembers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var owner = new User("brd-collab-arch", "brd-collab-arch@example.com", "hash");
        var collaborator = new User("brd-collab-archguest", "brd-collab-archguest@example.com", "hash");
        db.Users.AddRange(owner, collaborator);

        var board = new Board("Archived shared board", ownerId: owner.Id);
        board.Archive();
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        db.BoardAccesses.Add(new BoardAccess(board.Id, collaborator.Id, UserRole.Editor, owner.Id));
        await db.SaveChangesAsync();

        (await repo.CountCollaborationMembersAsync(owner.Id)).Should().Be(2);
    }

    [Fact]
    public async Task CountCollaborationMembersAsync_ShouldCountGranteeOnly_ForAnOwnerlessBoard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var grantee = new User("brd-collab-legacy", "brd-collab-legacy@example.com", "hash");
        db.Users.Add(grantee);

        var board = new Board("Legacy ownerless board");
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        db.BoardAccesses.Add(new BoardAccess(board.Id, grantee.Id, UserRole.Editor, grantee.Id));
        await db.SaveChangesAsync();

        (await repo.CountCollaborationMembersAsync(grantee.Id)).Should().Be(1);
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyList<string> Snapshot() => _commands.ToArray();

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
