using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArchivedBoardResourceConcurrencyTests
{
    [Theory]
    [InlineData("column-create", 1)]
    [InlineData("column-update", 1)]
    [InlineData("column-delete", 1)]
    [InlineData("column-reorder", 1)]
    [InlineData("column-reorder-one", 1)]
    [InlineData("label-create", 1)]
    [InlineData("label-update", 1)]
    [InlineData("label-delete", 1)]
    [InlineData("column-reorder", 2)]
    [InlineData("column-reorder-one", 2)]
    public async Task ArchiveBeforeSave_RejectsThatPhaseAndPreservesArchivedResources(string operation, int archiveBeforeSave)
    {
        await using var fixture = await Fixture.CreateAsync();
        Resources? atArchive = null;
        Guid? archiveToken = null;
        await using var writer = fixture.CreateWriter(async save =>
        {
            if (save != archiveBeforeSave) return;
            atArchive = await fixture.ReadResourcesAsync();
            archiveToken = await fixture.ArchiveAsync();
        });

        var result = await writer.MutateAsync(operation);

        archiveToken.Should().NotBeNull("the archive must commit before the tested save, without timing or sleeps");
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        (await fixture.ReadResourcesAsync()).Should().BeEquivalentTo(atArchive);
        await using var verify = new TaskdeckDbContext(fixture.Options);
        var archived = await verify.Boards.SingleAsync(board => board.Id == fixture.BoardId);
        archived.IsArchived.Should().BeTrue();
        archived.ConcurrencyToken.Should().Be(archiveToken!.Value);
        writer.Realtime.Verify(notifier => notifier.NotifyBoardMutationAsync(
            It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        writer.History.Verify(history => history.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task IndependentResourceWriters_WithTheSameBoardToken_BothPersist()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var first = fixture.CreateWriter();
        await using var second = fixture.CreateWriter();
        var firstBoard = await first.Db.Boards.SingleAsync(board => board.Id == fixture.BoardId);
        var secondBoard = await second.Db.Boards.SingleAsync(board => board.Id == fixture.BoardId);
        var token = firstBoard.ConcurrencyToken;
        var stamp = firstBoard.UpdatedAt;
        secondBoard.ConcurrencyToken.Should().Be(token);

        (await first.MutateAsync("column-update")).IsSuccess.Should().BeTrue();
        (await second.MutateAsync("label-update")).IsSuccess.Should().BeTrue();

        var resources = await fixture.ReadResourcesAsync();
        resources.Columns.Single(column => column.Id == fixture.FirstColumnId).Name.Should().Be("Updated column");
        resources.Labels.Single().Name.Should().Be("Updated label");
        await using var verify = new TaskdeckDbContext(fixture.Options);
        var board = await verify.Boards.SingleAsync(board => board.Id == fixture.BoardId);
        board.ConcurrencyToken.Should().Be(token, "dependent writes must not serialize unrelated resources");
        board.UpdatedAt.Should().Be(stamp, "board metadata has not changed");
        board.IsArchived.Should().BeFalse();
    }

    // Real repositories and EF/SQLite run both writes. Only the existing UoW save
    // boundary is decorated to commit the archive deterministically and translate
    // concurrency exceptions exactly as the production UnitOfWork does.
    private sealed class Writer : IAsyncDisposable
    {
        private readonly Fixture _fixture;
        private readonly ColumnService _columns;
        private readonly LabelService _labels;
        public TaskdeckDbContext Db { get; }
        public Mock<IBoardRealtimeNotifier> Realtime { get; } = new();
        public Mock<IHistoryService> History { get; } = new();

        public Writer(Fixture fixture, Func<int, Task>? beforeSave)
        {
            _fixture = fixture;
            Db = new TaskdeckDbContext(fixture.Options);
            var unit = new Mock<IUnitOfWork>();
            unit.SetupGet(work => work.Boards).Returns(new BoardRepository(Db));
            unit.SetupGet(work => work.Columns).Returns(new ColumnRepository(Db));
            unit.SetupGet(work => work.Labels).Returns(new LabelRepository(Db));
            var saves = 0;
            unit.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    saves++;
                    if (beforeSave is not null) await beforeSave(saves);
                    try { return await Db.SaveChangesAsync(cancellationToken); }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new DomainException(ErrorCodes.Conflict,
                            "Record was updated by another session. Refresh and retry your action.", exception);
                    }
                });
            Realtime.Setup(notifier => notifier.NotifyBoardMutationAsync(
                It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            History.Setup(history => history.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>())).ReturnsAsync(Result.Success());
            _columns = new ColumnService(unit.Object, Realtime.Object, History.Object);
            _labels = new LabelService(unit.Object, Realtime.Object, History.Object);
        }

        public async Task<Result> MutateAsync(string operation)
        {
            try
            {
                return operation switch
                {
                    "column-create" => await _columns.CreateColumnAsync(new CreateColumnDto(_fixture.BoardId, "New column", 2, null)),
                    "column-update" => await _columns.UpdateColumnAsync(_fixture.FirstColumnId, new UpdateColumnDto("Updated column", null, 8)),
                    "column-delete" => await _columns.DeleteColumnAsync(_fixture.FirstColumnId),
                    "column-reorder" => await _columns.ReorderColumnsAsync(_fixture.BoardId,
                        new ReorderColumnsDto(new List<Guid> { _fixture.SecondColumnId, _fixture.FirstColumnId })),
                    "column-reorder-one" => await _columns.ReorderColumnAsync(_fixture.FirstColumnId, 1),
                    "label-create" => await _labels.CreateLabelAsync(new CreateLabelDto(_fixture.BoardId, "New label", "#abcdef")),
                    "label-update" => await _labels.UpdateLabelAsync(_fixture.LabelId, new UpdateLabelDto("Updated label", "#abcdef")),
                    "label-delete" => await _labels.DeleteLabelAsync(_fixture.LabelId),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };
            }
            catch (DomainException exception)
            {
                // Delete methods propagate to the common HTTP error boundary;
                // other methods return the same domain code through Result.
                return Result.Failure(exception.ErrorCode, exception.Message);
            }
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed record ColumnState(Guid Id, string Name, int Position, int? WipLimit, DateTimeOffset UpdatedAt);
    private sealed record LabelState(Guid Id, string Name, string ColorHex, DateTimeOffset UpdatedAt);
    private sealed record Resources(ColumnState[] Columns, LabelState[] Labels);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"taskdeck-archive-resource-race-{Guid.NewGuid():N}.db");
        public DbContextOptions<TaskdeckDbContext> Options { get; }
        public Guid BoardId { get; private set; }
        public Guid FirstColumnId { get; private set; }
        public Guid SecondColumnId { get; private set; }
        public Guid LabelId { get; private set; }

        private Fixture()
        {
            Options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(_path)).Options;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try
            {
                await using var db = new TaskdeckDbContext(fixture.Options);
                await db.Database.MigrateAsync();
                var board = new Board("Resource race");
                var first = new Column(board.Id, "First", 0, 3);
                var second = new Column(board.Id, "Second", 1, 5);
                var label = new Label(board.Id, "Original label", "#123456");
                db.AddRange(board, first, second, label);
                await db.SaveChangesAsync();
                fixture.BoardId = board.Id;
                fixture.FirstColumnId = first.Id;
                fixture.SecondColumnId = second.Id;
                fixture.LabelId = label.Id;
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public Writer CreateWriter(Func<int, Task>? beforeSave = null) => new(this, beforeSave);

        public async Task<Guid> ArchiveAsync()
        {
            await using var db = new TaskdeckDbContext(Options);
            var board = await db.Boards.SingleAsync(candidate => candidate.Id == BoardId);
            board.Archive();
            await db.SaveChangesAsync();
            return board.ConcurrencyToken;
        }

        public async Task<Resources> ReadResourcesAsync()
        {
            await using var db = new TaskdeckDbContext(Options);
            var columns = await db.Columns.Where(column => column.BoardId == BoardId).ToListAsync();
            var labels = await db.Labels.Where(label => label.BoardId == BoardId).ToListAsync();
            return new Resources(
                columns.OrderBy(column => column.Id).Select(column => new ColumnState(
                    column.Id, column.Name, column.Position, column.WipLimit, column.UpdatedAt)).ToArray(),
                labels.OrderBy(label => label.Id).Select(label => new LabelState(
                    label.Id, label.Name, label.ColorHex, label.UpdatedAt)).ToArray());
        }

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(_path + suffix); }
                catch (IOException) { /* Best-effort cleanup after all contexts close. */ }
            }
            return ValueTask.CompletedTask;
        }
    }
}
