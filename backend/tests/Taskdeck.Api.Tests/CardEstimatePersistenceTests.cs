using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardEstimatePersistenceTests
{
    [Fact]
    public async Task EstimateMigration_DefaultsOldCardsToUnknown_AndDownPreservesIdentityRelationshipsAndData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>().UseSqlite(connection).Options;
        await using var db = new TaskdeckDbContext(options);
        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(m => m.EndsWith("_AddCardEstimatedEffort"));
        index.Should().BeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        var owner = new User("effort-migration", "effort@example.com", "hash");
        var board = new Board("Keep board", ownerId: owner.Id);
        var column = new Column(board.Id, "Keep column", 0);
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var originalVersion = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        db.AddRange(owner, board, column);
        await db.SaveChangesAsync();
        foreach (var id in new[] { parent, child })
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Cards (Id, BoardId, ColumnId, Title, Description, DueDate, IsBlocked, BlockReason, IsArchived, Position, CreatedAt, UpdatedAt, WorkItemType, ParentCardId) VALUES ({id}, {board.Id}, {column.Id}, 'Retain', 'Evidence', '2026-09-20', 1, 'Waiting', 0, 3, {originalVersion}, {originalVersion}, 2, NULL)");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Cards SET ParentCardId={parent} WHERE Id={child}");
        db.Add(new CardAssignment(child, owner.Id, owner.Id));
        await db.SaveChangesAsync();

        await migrator.MigrateAsync(migrations[index]);
        db.Database.HasPendingModelChanges().Should().BeFalse();
        var cards = await db.Cards.ToListAsync();
        cards.Should().HaveCount(2).And.OnlyContain(c => c.EstimatedEffortMinutes == null);
        cards.Single(c => c.Id == child).SetEstimatedEffortMinutes(0);
        cards.Single(c => c.Id == parent).SetEstimatedEffortMinutes(90);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.Cards.SingleAsync(c => c.Id == child)).EstimatedEffortMinutes.Should().Be(0);
        (await db.Cards.SingleAsync(c => c.Id == parent)).EstimatedEffortMinutes.Should().Be(90);

        await migrator.MigrateAsync(migrations[index - 1]);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Cards WHERE Title='Retain' AND Description='Evidence' AND DueDate='2026-09-20' AND IsBlocked=1 AND BlockReason='Waiting' AND IsArchived=0 AND Position=3 AND WorkItemType=2").SingleAsync()).Should().Be(2);
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Cards WHERE Id={child} AND ParentCardId={parent} AND BoardId={board.Id} AND ColumnId={column.Id}").SingleAsync()).Should().Be(1);
        (await db.Set<CardAssignment>().CountAsync()).Should().Be(1);
        await migrator.MigrateAsync(migrations[index]);
        db.ChangeTracker.Clear();
        (await db.Cards.ToListAsync()).Should().OnlyContain(c => c.EstimatedEffortMinutes == null);
    }

    [Fact]
    public async Task CompetingEstimateSave_RollsBackLosingCardAndAudit_WithoutRealtime()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>().UseSqlite(connection).Options;
        Guid cardId;
        await using (var seed = new TaskdeckDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            var board = new Board("Concurrent estimates");
            var column = new Column(board.Id, "Next", 0);
            var card = new Card(board.Id, column.Id, "Retained");
            cardId = card.Id;
            seed.AddRange(board, column, card);
            await seed.SaveChangesAsync();
        }
        await using var first = new TaskdeckDbContext(options);
        await using var second = new TaskdeckDbContext(options);
        var firstCard = await first.Cards.SingleAsync(c => c.Id == cardId);
        var secondCard = await second.Cards.SingleAsync(c => c.Id == cardId);
        var firstBoard = await first.Boards.SingleAsync();
        var secondBoard = await second.Boards.SingleAsync();
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var firstService = Service(first, firstBoard, firstCard, new Mock<IBoardRealtimeNotifier>().Object);
        var secondService = Service(second, secondBoard, secondCard, notifier.Object);
        (await firstService.UpdateCardAsync(cardId, Patch(firstCard, 90))).IsSuccess.Should().BeTrue();
        Func<Task> losingSave = () => secondService.UpdateCardAsync(cardId, Patch(secondCard, 60));
        await losingSave.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verify = new TaskdeckDbContext(options);
        (await verify.Cards.SingleAsync()).EstimatedEffortMinutes.Should().Be(90);
        var audit = await verify.AuditLogs.ToListAsync();
        audit.Should().ContainSingle().Which.Changes.Should().Be("Estimated effort: unknown -> 1h 30m");
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static UpdateCardDto Patch(Card card, int minutes) =>
        new(null, null, null, null, null, null, card.UpdatedAt, EstimatedEffortMinutes: minutes);

    private static CardService Service(TaskdeckDbContext db, Board board, Card card, IBoardRealtimeNotifier notifier)
    {
        var boards = new Mock<IBoardRepository>();
        boards.Setup(r => r.GetByIdAsync(board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(board);
        var cards = new Mock<ICardRepository>();
        cards.Setup(r => r.GetByIdWithLabelsAsync(card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(card);
        var audit = new Mock<IAuditLogRepository>();
        audit.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns<AuditLog, CancellationToken>((log, _) => { db.AuditLogs.Add(log); return Task.FromResult(log); });
        var work = new Mock<IUnitOfWork>();
        work.SetupGet(w => w.Boards).Returns(boards.Object);
        work.SetupGet(w => w.Cards).Returns(cards.Object);
        work.SetupGet(w => w.AuditLogs).Returns(audit.Object);
        work.Setup(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => db.SaveChangesAsync(ct));
        return new CardService(work.Object, notifier);
    }
}
