using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class TranscriptRepositoryIntegrationTests
{
    [Fact]
    public async Task GetByUserAsync_IsUserScopedAndPaginatesInDeterministicIdOrder()
    {
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "transcript-owner");
            var other = AddUser(db, "transcript-other");
            var first = AddTranscript(owner.Id, "first");
            var second = AddTranscript(owner.Id, "second");
            var third = AddTranscript(owner.Id, "third");
            db.Transcripts.AddRange(third, first, second, AddTranscript(other.Id, "private"));
            await db.SaveChangesAsync();

            var expectedIds = await db.Transcripts
                .Where(transcript => transcript.UserId == owner.Id)
                .OrderBy(transcript => transcript.Id)
                .Select(transcript => transcript.Id)
                .ToListAsync();

            var repository = new TranscriptRepository(db);
            var firstPage = await repository.GetByUserAsync(owner.Id, limit: 2, offset: 0);
            var secondPage = await repository.GetByUserAsync(owner.Id, limit: 2, offset: 2);
            var foreignLookup = await repository.GetByIdForUserAsync(
                db.Transcripts.Single(transcript => transcript.UserId == other.Id).Id,
                owner.Id);

            firstPage.Select(transcript => transcript.Id).Should().Equal(expectedIds.Take(2));
            secondPage.Select(transcript => transcript.Id).Should().Equal(expectedIds.Skip(2));
            foreignLookup.Should().BeNull("a transcript must never be readable through another user scope");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task DeleteByUserIdAsync_DeletesOnlyThatUsersTranscripts()
    {
        var dbPath = CreateDbPath();
        try
        {
            await using var db = new TaskdeckDbContext(CreateOptions(dbPath));
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "transcript-delete-owner");
            var other = AddUser(db, "transcript-delete-other");
            db.Transcripts.AddRange(AddTranscript(owner.Id, "delete me"), AddTranscript(other.Id, "retain me"));
            await db.SaveChangesAsync();

            var deleted = await new TranscriptRepository(db).DeleteByUserIdAsync(owner.Id);

            deleted.Should().Be(1);
            (await db.Transcripts.CountAsync(transcript => transcript.UserId == owner.Id)).Should().Be(0);
            (await db.Transcripts.CountAsync(transcript => transcript.UserId == other.Id)).Should().Be(1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static DbContextOptions<TaskdeckDbContext> CreateOptions(string dbPath) =>
        new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(dbPath))
            .Options;

    private static string CreateDbPath() => Path.Combine(Path.GetTempPath(), $"taskdeck-transcript-{Guid.NewGuid():N}.db");

    private static User AddUser(TaskdeckDbContext db, string username)
    {
        var user = new User(username, $"{username}@example.test", "hash");
        db.Users.Add(user);
        return user;
    }

    private static Transcript AddTranscript(Guid userId, string text)
    {
        var transcript = new Transcript(
            userId,
            CaptureSource.TranscriptPaste,
            text,
            [new TranscriptSegment(0, 0, "Speaker", 0)]);
        return transcript;
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
