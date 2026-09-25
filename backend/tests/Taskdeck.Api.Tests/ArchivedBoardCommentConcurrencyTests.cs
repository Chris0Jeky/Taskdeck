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
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArchivedBoardCommentConcurrencyTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task ArchiveBeforeSave_RejectsCommentAndAllStagedSideEffects(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        object? atArchive = null;
        Guid? archiveToken = null;
        await using var writer = new Writer(fixture, async () =>
        {
            atArchive = await fixture.SnapshotAsync();
            await using var archiveDb = new TaskdeckDbContext(fixture.Options);
            var board = await archiveDb.Boards.SingleAsync();
            board.Archive();
            await archiveDb.SaveChangesAsync();
            archiveToken = board.ConcurrencyToken;
        });

        var result = await writer.MutateAsync(operation);

        writer.SaveCalls.Should().Be(1);
        archiveToken.Should().NotBeNull("the competing archive must commit before the tested save");
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        (await fixture.SnapshotAsync()).Should().BeEquivalentTo(atArchive);
        await using var verify = new TaskdeckDbContext(fixture.Options);
        var archived = await verify.Boards.SingleAsync();
        archived.IsArchived.Should().BeTrue();
        archived.ConcurrencyToken.Should().Be(archiveToken!.Value);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task ActiveBoard_PersistsTheCommentAndMentionEffectsInOneSave(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var writer = new Writer(fixture);

        (await writer.MutateAsync(operation)).IsSuccess.Should().BeTrue();

        writer.SaveCalls.Should().Be(1);
        await using var verify = new TaskdeckDbContext(fixture.Options);
        (await verify.AuditLogs.CountAsync()).Should().Be(1);
        if (operation == "delete")
        {
            (await verify.CardComments.SingleAsync()).IsDeleted.Should().BeTrue();
            (await verify.Notifications.CountAsync()).Should().Be(0);
        }
        else
        {
            (await verify.CardComments.CountAsync(comment => comment.Content == "After @reader")).Should().Be(1);
            (await verify.CardCommentMentions.CountAsync()).Should().Be(1);
            (await verify.Notifications.CountAsync()).Should().Be(1);
            (await verify.NotificationPreferences.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task IndependentCommentWriters_KeepBoardMetadataAndBothPersist()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var first = new Writer(fixture);
        await using var second = new Writer(fixture);
        var firstBoard = await first.Db.Boards.SingleAsync();
        var secondBoard = await second.Db.Boards.SingleAsync();
        var token = firstBoard.ConcurrencyToken;
        var stamp = firstBoard.UpdatedAt;
        secondBoard.ConcurrencyToken.Should().Be(token);

        (await first.MutateAsync("create")).IsSuccess.Should().BeTrue();
        (await second.MutateAsync("create")).IsSuccess.Should().BeTrue();

        await using var verify = new TaskdeckDbContext(fixture.Options);
        (await verify.CardComments.CountAsync()).Should().Be(3);
        var board = await verify.Boards.SingleAsync();
        board.ConcurrencyToken.Should().Be(token);
        board.UpdatedAt.Should().Be(stamp);
        board.IsArchived.Should().BeFalse();
    }

    // The real notification service stages notifications/preferences rather than
    // saving them. Decorate only the shared save boundary for deterministic race
    // injection; real EF repositories and SQLite persist or roll back all effects.
    private sealed class Writer : IAsyncDisposable
    {
        private readonly Fixture _fixture;
        private readonly CardCommentService _service;
        public TaskdeckDbContext Db { get; }
        public int SaveCalls { get; private set; }

        public Writer(Fixture fixture, Func<Task>? beforeSave = null)
        {
            _fixture = fixture;
            Db = new TaskdeckDbContext(fixture.Options);
            var unit = new Mock<IUnitOfWork>();
            unit.SetupGet(work => work.Boards).Returns(new BoardRepository(Db));
            unit.SetupGet(work => work.Cards).Returns(new CardRepository(Db));
            unit.SetupGet(work => work.CardComments).Returns(new CardCommentRepository(Db));
            unit.SetupGet(work => work.Users).Returns(new UserRepository(Db));
            unit.SetupGet(work => work.AuditLogs).Returns(new AuditLogRepository(Db));
            unit.SetupGet(work => work.Notifications).Returns(new NotificationRepository(Db));
            unit.SetupGet(work => work.NotificationPreferences).Returns(new NotificationPreferenceRepository(Db));
            unit.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    SaveCalls++;
                    if (beforeSave is not null) await beforeSave();
                    try { return await Db.SaveChangesAsync(cancellationToken); }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new DomainException(ErrorCodes.Conflict,
                            "Record was updated by another session. Refresh and retry your action.", exception);
                    }
                });
            _service = new CardCommentService(unit.Object, new NotificationService(unit.Object));
        }

        public async Task<Result> MutateAsync(string operation)
        {
            try
            {
                return operation switch
                {
                    "create" => await _service.CreateCommentAsync(_fixture.BoardId, _fixture.CardId, _fixture.AuthorId,
                        new CreateCardCommentDto("After @reader")),
                    "update" => await _service.UpdateCommentAsync(_fixture.BoardId, _fixture.CardId, _fixture.CommentId,
                        _fixture.AuthorId, new UpdateCardCommentDto("After @reader")),
                    "delete" => await _service.DeleteCommentAsync(_fixture.BoardId, _fixture.CardId, _fixture.CommentId, _fixture.AuthorId),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };
            }
            catch (DomainException exception) { return Result.Failure(exception.ErrorCode, exception.Message); }
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"taskdeck-comment-archive-race-{Guid.NewGuid():N}.db");
        public DbContextOptions<TaskdeckDbContext> Options { get; }
        public Guid BoardId { get; private set; }
        public Guid CardId { get; private set; }
        public Guid CommentId { get; private set; }
        public Guid AuthorId { get; private set; }

        private Fixture() => Options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(_path)).Options;

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try
            {
                await using var db = new TaskdeckDbContext(fixture.Options);
                await db.Database.MigrateAsync();
                var author = new User("author", "author@example.test", "hash");
                var reader = new User("reader", "reader@example.test", "hash");
                var board = new Board("Comment race", ownerId: author.Id);
                var column = new Column(board.Id, "Todo", 0);
                var card = new Card(board.Id, column.Id, "Card");
                var comment = new CardComment(card.Id, board.Id, author.Id, "Before");
                db.AddRange(author, reader, board, column, card, comment);
                await db.SaveChangesAsync();
                fixture.BoardId = board.Id; fixture.CardId = card.Id;
                fixture.CommentId = comment.Id; fixture.AuthorId = author.Id;
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task<object> SnapshotAsync()
        {
            await using var db = new TaskdeckDbContext(Options);
            return new
            {
                Comments = await db.CardComments.AsNoTracking().Select(comment => new
                {
                    comment.Id, comment.Content, comment.IsDeleted, comment.CreatedAt,
                    comment.UpdatedAt, comment.EditedAt, comment.DeletedAt
                }).ToArrayAsync(),
                Mentions = await db.CardCommentMentions.AsNoTracking().Select(mention => new
                {
                    mention.Id, mention.CardCommentId, mention.MentionedUserId, mention.MentionedUsername
                }).ToArrayAsync(),
                Notifications = await db.Notifications.Select(notification => notification.Id).ToArrayAsync(),
                Preferences = await db.NotificationPreferences.Select(preference => preference.Id).ToArrayAsync(),
                Audits = await db.AuditLogs.Select(audit => audit.Id).ToArrayAsync()
            };
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
