using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ChatSessionRepositoryConcurrencyTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"taskdeck-chat-bind-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task TryBindBoardAsync_ConcurrentSameBoard_LoserRereadsAuthoritativeBinding()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(_dbPath))
            .Options;

        Guid userId;
        Guid boardId;
        Guid sessionId;
        await using (var seedDb = new TaskdeckDbContext(options))
        {
            await seedDb.Database.MigrateAsync();
            var user = new User(
                $"chat-bind-{Guid.NewGuid():N}"[..20],
                $"{Guid.NewGuid():N}@example.com",
                "hash");
            var board = new Board("Concurrent binding", ownerId: user.Id);
            var session = new ChatSession(user.Id, "Concurrent binding");
            seedDb.AddRange(user, board, session);
            await seedDb.SaveChangesAsync();
            (userId, boardId, sessionId) = (user.Id, board.Id, session.Id);
        }

        await using var firstDb = new TaskdeckDbContext(options);
        await using var secondDb = new TaskdeckDbContext(options);
        var firstRepository = new ChatSessionRepository(firstDb);
        var secondRepository = new ChatSessionRepository(secondDb);

        // Both requests cross the read barrier with the same tracked, unbound state before either CAS.
        (await firstRepository.GetByIdWithMessagesAsync(sessionId))!.BoardId.Should().BeNull();
        (await secondRepository.GetByIdWithMessagesAsync(sessionId))!.BoardId.Should().BeNull();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAttempt = Task.Run(async () =>
        {
            await start.Task;
            return await firstRepository.TryBindBoardAsync(
                sessionId, userId, boardId, DateTimeOffset.UtcNow);
        });
        var secondAttempt = Task.Run(async () =>
        {
            await start.Task;
            return await secondRepository.TryBindBoardAsync(
                sessionId, userId, boardId, DateTimeOffset.UtcNow);
        });

        start.SetResult();
        var results = await Task.WhenAll(firstAttempt, secondAttempt);
        results.Should().ContainSingle(result => result);

        var losingRepository = results[0] ? secondRepository : firstRepository;
        var authoritative = await losingRepository.GetByIdWithMessagesAsync(sessionId);
        authoritative!.BoardId.Should().Be(
            boardId,
            "the CAS loser must not resolve an idempotent same-board race from its stale tracked entity");
    }

    [Fact]
    public async Task GetByIdWithMessagesAsync_ShouldKeepTrackedNavigationChronological_AfterDescendingFixup()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(_dbPath))
            .Options;

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var seedDb = new TaskdeckDbContext(options))
        {
            await seedDb.Database.MigrateAsync();
            var user = new User(
                $"chat-order-{Guid.NewGuid():N}"[..20],
                $"chat-order-{Guid.NewGuid():N}@example.com",
                "hash");
            var session = new ChatSession(user.Id, "Ordered history");
            typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(session, sessionId);
            var oldest = new ChatMessage(session.Id, ChatMessageRole.User, "Original instruction");
            var clarification = new ChatMessage(
                session.Id,
                ChatMessageRole.Assistant,
                "What should I call it?",
                "clarification");
            var answer = new ChatMessage(session.Id, ChatMessageRole.User, "Ship notes");
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-3);
            SetCreatedAt(oldest, baseTime);
            SetCreatedAt(clarification, baseTime.AddMinutes(1));
            SetCreatedAt(answer, baseTime.AddMinutes(2));
            session.AddMessage(oldest);
            session.AddMessage(clarification);
            session.AddMessage(answer);
            seedDb.AddRange(user, session);
            await seedDb.SaveChangesAsync();
            userId = user.Id;
        }

        await using var db = new TaskdeckDbContext(options);
        var trackedSession = await db.ChatSessions.FindAsync(sessionId);
        trackedSession.Should().NotBeNull();
        trackedSession!.UserId.Should().Be(userId);

        // SQLite cannot translate DateTimeOffset ordering. Use the persisted column directly to
        // reproduce descending materialization and let EF relationship fixup populate the tracked
        // session navigation in that order.
        await db.ChatMessages
            .FromSqlInterpolated($"SELECT * FROM ChatMessages WHERE SessionId = {sessionId} ORDER BY CreatedAt DESC")
            .LoadAsync();

        trackedSession.Messages.Select(message => message.Content).Should().Equal(
            "Original instruction",
            "What should I call it?",
            "Ship notes");
    }

    private static void SetCreatedAt(Entity entity, DateTimeOffset timestamp)
        => typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(entity, timestamp);

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup only; Windows can briefly retain the SQLite handle.
        }
    }
}
