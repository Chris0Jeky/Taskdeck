using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for <see cref="SourceArtefactRepository.GetContentsForUserAsync"/> against
/// real SQLite (#1355). Verifies the batched blob load resolves every requested artefact in a
/// single round-trip, stays user-scoped, and is valid for the empty-set edge.
/// </summary>
public sealed class SourceArtefactRepositoryIntegrationTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task GetContentsForUserAsync_WithEmptyIdSet_ReturnsEmptyAndIssuesNoQuery()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var user = AddUser(db, "artefact-empty");
            await db.SaveChangesAsync();

            var repo = new SourceArtefactRepository(db);
            interceptor.Clear();

            var result = await repo.GetContentsForUserAsync(Array.Empty<Guid>(), user.Id);

            result.Should().BeEmpty();
            BlobSelects(interceptor).Should().BeEmpty(
                "an empty id set must short-circuit before touching the database");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetContentsForUserAsync_LoadsAllRequestedBlobs_InASingleRoundTrip()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var user = AddUser(db, "artefact-batch");

            var seeded = new Dictionary<Guid, byte[]>();
            for (var i = 0; i < 25; i++)
            {
                var content = Encoding.UTF8.GetBytes($"blob-content-{i}");
                var artefact = AddArtefact(db, user.Id, $"a{i}.txt", content);
                seeded[artefact.Id] = content;
            }
            await db.SaveChangesAsync();

            var repo = new SourceArtefactRepository(db);
            interceptor.Clear();

            var result = await repo.GetContentsForUserAsync(seeded.Keys.ToList(), user.Id);

            result.Should().HaveCount(25);
            foreach (var (id, content) in seeded)
                result[id].Should().Equal(content);

            // The whole point of #1355: one SELECT, not one per artefact.
            BlobSelects(interceptor).Should().HaveCount(1,
                "batched blob loads must resolve every artefact in a single query");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetContentsForUserAsync_NeverReturnsAnotherUsersBlob()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "artefact-owner");
            var other = AddUser(db, "artefact-other");

            var ownerContent = Encoding.UTF8.GetBytes("owner blob");
            var ownerArtefact = AddArtefact(db, owner.Id, "owned.txt", ownerContent);
            var otherArtefact = AddArtefact(db, other.Id, "foreign.txt", Encoding.UTF8.GetBytes("foreign blob"));
            await db.SaveChangesAsync();

            var repo = new SourceArtefactRepository(db);

            // Request BOTH ids while scoped to the owner — the foreign artefact must be absent.
            var result = await repo.GetContentsForUserAsync(
                new[] { ownerArtefact.Id, otherArtefact.Id },
                owner.Id);

            result.Should().ContainKey(ownerArtefact.Id);
            result[ownerArtefact.Id].Should().Equal(ownerContent);
            result.Should().NotContainKey(otherArtefact.Id,
                "user-scoping must exclude another user's artefact even when its id is requested");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetContentsForUserAsync_OmitsUnknownIds_WithoutError()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var user = AddUser(db, "artefact-unknown");
            var content = Encoding.UTF8.GetBytes("present");
            var artefact = AddArtefact(db, user.Id, "present.txt", content);
            await db.SaveChangesAsync();

            var repo = new SourceArtefactRepository(db);
            var missing = Guid.NewGuid();

            var result = await repo.GetContentsForUserAsync(new[] { artefact.Id, missing }, user.Id);

            result.Should().ContainKey(artefact.Id);
            result.Should().NotContainKey(missing);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (DbContextOptions<TaskdeckDbContext> Options, CapturingCommandInterceptor Interceptor, string DbPath) CreateSqliteOptions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-artefact-batch-{Guid.NewGuid():N}.db");
        var interceptor = new CapturingCommandInterceptor();
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .AddInterceptors(interceptor)
            .Options;
        return (options, interceptor, dbPath);
    }

    private static User AddUser(TaskdeckDbContext db, string handle)
    {
        var user = new User(handle, $"{handle}@example.com", "hash");
        db.Users.Add(user);
        return user;
    }

    private static SourceArtefact AddArtefact(TaskdeckDbContext db, Guid userId, string fileName, byte[] content)
    {
        var artefact = new SourceArtefact(
            userId,
            ArtefactKind.TextFile,
            "text/plain",
            fileName,
            content.Length,
            Sha,
            CaptureSource.Import);
        db.SourceArtefacts.Add(artefact);
        db.ArtefactBlobs.Add(new ArtefactBlob(artefact.Id, content));
        return artefact;
    }

    private static IReadOnlyList<string> BlobSelects(CapturingCommandInterceptor interceptor) =>
        interceptor.CapturedCommands
            .Where(sql => sql.Contains("ArtefactBlobs", StringComparison.OrdinalIgnoreCase)
                          && sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = dbPath + suffix;
            if (!File.Exists(path))
                continue;

            try { File.Delete(path); }
            catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    /// <summary>
    /// Records the text of every reader command EF executes so a test can count the SELECTs that
    /// actually reached SQLite (proving the batch load is one round-trip, not one per artefact).
    /// </summary>
    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyCollection<string> CapturedCommands => _commands;

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
