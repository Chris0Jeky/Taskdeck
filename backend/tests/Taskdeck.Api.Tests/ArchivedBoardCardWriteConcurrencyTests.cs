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
}
