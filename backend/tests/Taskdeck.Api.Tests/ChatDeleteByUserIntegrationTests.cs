using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Proves the set-based account-deletion chat cleanup deletes exactly the owner's rows:
/// user-scoped, exact counts, and complete past any fetch-cap volume (the old per-row loop
/// silently kept everything beyond its 100k fetch cap).
/// </summary>
public sealed class ChatDeleteByUserIntegrationTests
{
    [Fact]
    public async Task DeleteByUserIdAsync_DeletesOnlyTheOwnersMessagesAndSessions_WithExactCounts()
    {
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "chat-owner");
            var other = AddUser(db, "chat-other");
            var ownerSession = new ChatSession(owner.Id, "owner session");
            var otherSession = new ChatSession(other.Id, "other session");
            db.ChatSessions.AddRange(ownerSession, otherSession);
            await db.SaveChangesAsync();
            db.ChatMessages.AddRange(
                new ChatMessage(ownerSession.Id, ChatMessageRole.User, "hello"),
                new ChatMessage(ownerSession.Id, ChatMessageRole.Assistant, "hi"),
                new ChatMessage(otherSession.Id, ChatMessageRole.User, "private"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var messageRepository = new ChatMessageRepository(db);
            var sessionRepository = new ChatSessionRepository(db);
            var messagesDeleted = await messageRepository.DeleteByUserIdAsync(owner.Id);
            var sessionsDeleted = await sessionRepository.DeleteByUserIdAsync(owner.Id);

            messagesDeleted.Should().Be(2);
            sessionsDeleted.Should().Be(1);
            (await db.ChatMessages.CountAsync()).Should().Be(1);
            (await db.ChatSessions.CountAsync()).Should().Be(1);
            (await db.ChatMessages.SingleAsync()).SessionId.Should().Be(otherSession.Id);
            (await db.ChatSessions.SingleAsync()).UserId.Should().Be(other.Id);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public async Task DeleteByUserIdAsync_RemovesEveryRow_PastBatchVolumes()
    {
        // ExecuteDeleteAsync issues one set-based statement: unlike the old capped fetch
        // loop, there is no volume at which rows silently survive. 2501 rows exercises a
        // multi-thousand delete deterministically without timing sensitivity.
        const int messageCount = 2501;
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "chat-volume-owner");
            var session = new ChatSession(owner.Id, "volume session");
            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();
            for (var i = 0; i < messageCount; i++)
                db.ChatMessages.Add(new ChatMessage(session.Id, ChatMessageRole.User, $"message {i}"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var messageRepository = new ChatMessageRepository(db);
            var sessionRepository = new ChatSessionRepository(db);
            var messagesDeleted = await messageRepository.DeleteByUserIdAsync(owner.Id);
            var sessionsDeleted = await sessionRepository.DeleteByUserIdAsync(owner.Id);

            messagesDeleted.Should().Be(messageCount);
            sessionsDeleted.Should().Be(1);
            (await db.ChatMessages.CountAsync()).Should().Be(0);
            (await db.ChatSessions.CountAsync()).Should().Be(0);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public async Task DeleteByUserIdAsync_WithNoRows_DeletesNothing()
    {
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "chat-empty-owner");

            var messageRepository = new ChatMessageRepository(db);
            var sessionRepository = new ChatSessionRepository(db);

            (await messageRepository.DeleteByUserIdAsync(owner.Id)).Should().Be(0);
            (await sessionRepository.DeleteByUserIdAsync(owner.Id)).Should().Be(0);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    private static User AddUser(TaskdeckDbContext db, string username)
    {
        var user = new User(username, $"{username}@example.com", "hash");
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static string CreateDbPath()
        => Path.Combine(Path.GetTempPath(), $"taskdeck-chatdel-{Guid.NewGuid():N}.db");

    private static DbContextOptions<TaskdeckDbContext> CreateOptions(string dbPath)
        => new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString)
            .Options;

    private static void CleanupDb(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = dbPath + suffix;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best-effort temp cleanup; a leaked/locked handle is not a test failure.
            }
        }
    }
}
