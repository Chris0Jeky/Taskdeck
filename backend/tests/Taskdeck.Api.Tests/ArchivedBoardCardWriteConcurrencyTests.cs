using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArchivedBoardCardWriteConcurrencyTests
{
    [Fact]
    public async Task CreateCardAsync_WhenArchiveCommitsAfterTheBoardRead_DoesNotPersistTheCard()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-archived-card-race-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            var (boardId, columnId) = await SeedBoardAsync(options);

            await using var cardWriteDb = new TaskdeckDbContext(options);
            await using var archiveDb = new TaskdeckDbContext(options);
            var cardWriteBoard = await cardWriteDb.Boards.SingleAsync(board => board.Id == boardId);
            var cardWriteColumn = await cardWriteDb.Columns
                .Include(column => column.Cards)
                .SingleAsync(column => column.Id == columnId);
            var archiveBoard = await archiveDb.Boards.SingleAsync(board => board.Id == boardId);

            var boards = new Mock<IBoardRepository>();
            var columns = new Mock<IColumnRepository>();
            var cards = new Mock<ICardRepository>();
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);
            unitOfWork.SetupGet(work => work.Columns).Returns(columns.Object);
            unitOfWork.SetupGet(work => work.Cards).Returns(cards.Object);

            boards.Setup(repository => repository.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(cardWriteBoard);
            columns.Setup(repository => repository.GetByIdWithCardsAsync(columnId, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    archiveBoard.Archive();
                    await archiveDb.SaveChangesAsync();
                    return cardWriteColumn;
                });
            cards.Setup(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
                .Returns((Card card, CancellationToken _) =>
                {
                    cardWriteDb.Cards.Add(card);
                    return Task.FromResult(card);
                });
            unitOfWork.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    try
                    {
                        return await cardWriteDb.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new DomainException(
                            ErrorCodes.Conflict,
                            "Record was updated by another session. Refresh and retry your action.",
                            exception);
                    }
                });

            var service = new CardService(unitOfWork.Object);

            var result = await service.CreateCardAsync(
                new CreateCardDto(boardId, columnId, "Concurrent write", null, null, null));

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.Conflict);

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Boards.SingleAsync(board => board.Id == boardId)).IsArchived.Should().BeTrue();
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(0,
                "the card insert and stale board update share one SaveChanges transaction");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (!File.Exists(path))
                    continue;

                try { File.Delete(path); }
                catch (IOException) { /* best-effort temporary database cleanup */ }
            }
        }
    }

    [Fact]
    public async Task CreateCardAsync_WhenTwoWritersReadTheSameBoard_BothCardsPersist()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-card-create-race-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            var (boardId, columnId) = await SeedBoardAsync(options);

            await using var firstDb = new TaskdeckDbContext(options);
            await using var secondDb = new TaskdeckDbContext(options);
            var firstBoard = await firstDb.Boards.SingleAsync(board => board.Id == boardId);
            var secondBoard = await secondDb.Boards.SingleAsync(board => board.Id == boardId);
            var firstColumn = await firstDb.Columns.Include(column => column.Cards).SingleAsync(column => column.Id == columnId);
            var secondColumn = await secondDb.Columns.Include(column => column.Cards).SingleAsync(column => column.Id == columnId);

            var firstService = CreateCardService(firstDb, firstBoard, firstColumn);
            var secondService = CreateCardService(secondDb, secondBoard, secondColumn);

            var firstResult = await firstService.CreateCardAsync(
                new CreateCardDto(boardId, columnId, "First concurrent card", null, null, null));
            var secondResult = await secondService.CreateCardAsync(
                new CreateCardDto(boardId, columnId, "Second concurrent card", null, null, null));

            firstResult.IsSuccess.Should().BeTrue();
            secondResult.IsSuccess.Should().BeTrue();

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(2);
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task UpdateCardAsync_WithoutExpectedUpdatedAt_WhenTwoWritersReadTheSameBoard_LastWriterSucceeds()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-card-update-race-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            var (boardId, columnId, cardId) = await SeedBoardWithCardAsync(options);

            await using var firstDb = new TaskdeckDbContext(options);
            await using var secondDb = new TaskdeckDbContext(options);
            var firstBoard = await firstDb.Boards.SingleAsync(board => board.Id == boardId);
            var secondBoard = await secondDb.Boards.SingleAsync(board => board.Id == boardId);
            var firstColumn = await firstDb.Columns.SingleAsync(column => column.Id == columnId);
            var secondColumn = await secondDb.Columns.SingleAsync(column => column.Id == columnId);
            var firstCard = await firstDb.Cards.Include(card => card.CardLabels).SingleAsync(card => card.Id == cardId);
            var secondCard = await secondDb.Cards.Include(card => card.CardLabels).SingleAsync(card => card.Id == cardId);

            var firstService = CreateCardService(firstDb, firstBoard, firstColumn, firstCard);
            var secondService = CreateCardService(secondDb, secondBoard, secondColumn, secondCard);

            var firstResult = await firstService.UpdateCardAsync(
                cardId,
                new UpdateCardDto("First writer", null, null, null, null, null));
            var secondResult = await secondService.UpdateCardAsync(
                cardId,
                new UpdateCardDto("Last writer", null, null, null, null, null));

            firstResult.IsSuccess.Should().BeTrue();
            secondResult.IsSuccess.Should().BeTrue();

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Cards.SingleAsync(card => card.Id == cardId)).Title.Should().Be("Last writer");
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task RecordCardMutation_PutsTheBoardRowInTheUpdate_WithoutMovingUpdatedAt()
    {
        // `#2123`: the guard used to rely on Touch() re-stamping UpdatedAt. Inside one clock tick
        // that is a no-op, EF leaves the entity Unchanged, no board UPDATE is emitted, and the
        // concurrency-token predicate silently does not run. The marker column removes the clock
        // from the mechanism entirely, so this holds for every call rather than most of them.
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-card-guard-marker-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            var (boardId, _) = await SeedBoardAsync(options);

            await using var db = new TaskdeckDbContext(options);
            var board = await db.Boards.SingleAsync(entity => entity.Id == boardId);
            var updatedAtBefore = board.UpdatedAt;
            var tokenBefore = board.ConcurrencyToken;
            var markerBefore = board.CardMutationMarker;

            // Two back-to-back calls: the second is the same-tick case #2123 describes.
            board.RecordCardMutation();
            var entryAfterFirst = db.Entry(board);
            entryAfterFirst.State.Should().Be(EntityState.Modified,
                "the board row must join the card write's UPDATE so the token predicate runs");
            entryAfterFirst.Property(nameof(Board.CardMutationMarker)).IsModified.Should().BeTrue();
            entryAfterFirst.Property(nameof(Board.UpdatedAt)).IsModified.Should().BeFalse();
            entryAfterFirst.Property(nameof(Board.ConcurrencyToken)).IsModified.Should().BeFalse(
                "advancing the token here would make independent card writes conflict with each other");

            await db.SaveChangesAsync();

            board.RecordCardMutation();
            db.Entry(board).State.Should().Be(EntityState.Modified);
            await db.SaveChangesAsync();

            await using var verifyDb = new TaskdeckDbContext(options);
            var persisted = await verifyDb.Boards.SingleAsync(entity => entity.Id == boardId);
            persisted.CardMutationMarker.Should().Be(markerBefore + 2);
            persisted.UpdatedAt.Should().Be(updatedAtBefore, "UpdatedAt is the board's own metadata timestamp");
            persisted.ConcurrencyToken.Should().Be(tokenBefore);
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task CreateCardAsync_AfterAnEarlierCardWriteOnTheSameBoard_StillConflictsWithARacingArchive()
    {
        // The existing race regression starts from a board seeded long before the write. This one
        // starts from a board this same context already updated and saved once, so the second write
        // exercises the guard from a post-save tracking state (marker 1 -> 2) rather than a freshly
        // read one. It is NOT a reproduction of #2123's same-tick window: that window cannot be
        // forced without a clock seam, and the deterministic proof that the clock no longer gates the
        // guard is RecordCardMutation_PutsTheBoardRowInTheUpdate_WithoutMovingUpdatedAt above.
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-archived-card-race-backtoback-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;
            var (boardId, columnId) = await SeedBoardAsync(options);

            await using var cardWriteDb = new TaskdeckDbContext(options);
            await using var archiveDb = new TaskdeckDbContext(options);
            var cardWriteBoard = await cardWriteDb.Boards.SingleAsync(board => board.Id == boardId);
            var cardWriteColumn = await cardWriteDb.Columns
                .Include(column => column.Cards)
                .SingleAsync(column => column.Id == columnId);
            var archiveBoard = await archiveDb.Boards.SingleAsync(board => board.Id == boardId);

            var archiveOnNextColumnRead = false;
            var boards = new Mock<IBoardRepository>();
            var columns = new Mock<IColumnRepository>();
            var cards = new Mock<ICardRepository>();
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);
            unitOfWork.SetupGet(work => work.Columns).Returns(columns.Object);
            unitOfWork.SetupGet(work => work.Cards).Returns(cards.Object);

            boards.Setup(repository => repository.GetByIdAsync(boardId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(cardWriteBoard);
            columns.Setup(repository => repository.GetByIdWithCardsAsync(columnId, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    if (archiveOnNextColumnRead)
                    {
                        archiveBoard.Archive();
                        await archiveDb.SaveChangesAsync();
                    }

                    return cardWriteColumn;
                });
            cards.Setup(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
                .Returns((Card card, CancellationToken _) =>
                {
                    cardWriteDb.Cards.Add(card);
                    return Task.FromResult(card);
                });
            cards.Setup(repository => repository.GetByIdWithLabelsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid cardId, CancellationToken _) => cardWriteDb.Cards
                    .Include(card => card.CardLabels)
                    .SingleOrDefaultAsync(card => card.Id == cardId));
            unitOfWork.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    try
                    {
                        return await cardWriteDb.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new DomainException(
                            ErrorCodes.Conflict,
                            "Record was updated by another session. Refresh and retry your action.",
                            exception);
                    }
                });

            var service = new CardService(unitOfWork.Object);

            var first = await service.CreateCardAsync(
                new CreateCardDto(boardId, columnId, "First write", null, null, null));
            first.IsSuccess.Should().BeTrue();

            archiveOnNextColumnRead = true;
            var second = await service.CreateCardAsync(
                new CreateCardDto(boardId, columnId, "Racing write", null, null, null));

            second.IsSuccess.Should().BeFalse();
            second.ErrorCode.Should().Be(ErrorCodes.Conflict);

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Boards.SingleAsync(board => board.Id == boardId)).IsArchived.Should().BeTrue();
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(1,
                "only the pre-archive write survives");
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    private static CardService CreateCardService(
        TaskdeckDbContext db,
        Board board,
        Column column,
        Card? existingCard = null)
    {
        var boards = new Mock<IBoardRepository>();
        var columns = new Mock<IColumnRepository>();
        var cards = new Mock<ICardRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);
        unitOfWork.SetupGet(work => work.Columns).Returns(columns.Object);
        unitOfWork.SetupGet(work => work.Cards).Returns(cards.Object);

        boards.Setup(repository => repository.GetByIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        columns.Setup(repository => repository.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        cards.Setup(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
            .Returns((Card card, CancellationToken _) =>
            {
                db.Cards.Add(card);
                return Task.FromResult(card);
            });
        cards.Setup(repository => repository.GetByIdWithLabelsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid cardId, CancellationToken _) =>
            {
                if (existingCard?.Id == cardId)
                    return Task.FromResult<Card?>(existingCard);

                return db.Cards
                    .Include(card => card.CardLabels)
                    .SingleOrDefaultAsync(card => card.Id == cardId);
            });
        unitOfWork.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken));

        return new CardService(unitOfWork.Object);
    }

    private static async Task<(Guid BoardId, Guid ColumnId)> SeedBoardAsync(DbContextOptions<TaskdeckDbContext> options)
    {
        await using var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();

        var board = new Board("Concurrency board");
        var column = new Column(board.Id, "To Do", 0);
        db.Boards.Add(board);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        return (board.Id, column.Id);
    }

    private static async Task<(Guid BoardId, Guid ColumnId, Guid CardId)> SeedBoardWithCardAsync(DbContextOptions<TaskdeckDbContext> options)
    {
        await using var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();

        var board = new Board("Concurrency board");
        var column = new Column(board.Id, "To Do", 0);
        var card = new Card(board.Id, column.Id, "Original card", null, null, 0);
        db.AddRange(board, column, card);
        await db.SaveChangesAsync();

        return (board.Id, column.Id, card.Id);
    }

    private static void DeleteTemporaryDatabase(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = dbPath + suffix;
            if (!File.Exists(path))
                continue;

            try { File.Delete(path); }
            catch (IOException) { /* best-effort temporary database cleanup */ }
        }
    }
}
