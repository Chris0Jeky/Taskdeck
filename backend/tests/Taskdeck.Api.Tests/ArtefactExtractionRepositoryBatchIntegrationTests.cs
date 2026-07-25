using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for <see cref="ArtefactExtractionRepository.GetByArtefactsForUserAsync"/>
/// against real SQLite (#1387). Verifies the batched extraction-history load resolves every
/// requested artefact in a single round-trip, is byte-for-byte order-identical to the former
/// per-artefact sequential paging (including the CreatedAt/Id-as-TEXT tiebreak across the 50-row
/// page boundary), stays user-scoped, and honours the empty-set and batch-cap edges.
/// </summary>
public sealed class ArtefactExtractionRepositoryBatchIntegrationTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task GetByArtefactsForUserAsync_MatchesSequentialPaging_InASingleRoundTrip()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var user = AddUser(db, "extraction-batch");

            // Two owned artefacts: one whose history crosses the 50-row page boundary twice, one
            // small. Some extractions deliberately share a CreatedAt to exercise the Id tiebreak —
            // a naive in-memory Guid sort would diverge from the raw-SQL TEXT ordering here.
            var heavy = AddArtefact(db, user.Id, "heavy.txt");
            var light = AddArtefact(db, user.Id, "light.txt");
            var baseTime = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 120; i++)
                AddExtraction(db, heavy.Id, $"heavy-{i}", baseTime.AddMinutes(i / 2)); // pairs share a timestamp
            for (var i = 0; i < 3; i++)
                AddExtraction(db, light.Id, $"light-{i}", baseTime.AddMinutes(i));
            await db.SaveChangesAsync();

            var repo = new ArtefactExtractionRepository(db);

            // Reference: reproduce the former per-artefact path (page limit 50, accumulate all).
            var sequentialHeavy = await ReadAllSequentiallyAsync(repo, heavy.Id, user.Id);
            var sequentialLight = await ReadAllSequentiallyAsync(repo, light.Id, user.Id);

            interceptor.Clear();
            var batch = await repo.GetByArtefactsForUserAsync(new[] { heavy.Id, light.Id }, user.Id);

            // Byte-for-byte ordering parity, id-for-id, including across the page boundary.
            batch[heavy.Id].Select(e => e.Id).Should().Equal(sequentialHeavy.Select(e => e.Id));
            batch[light.Id].Select(e => e.Id).Should().Equal(sequentialLight.Select(e => e.Id));
            batch[heavy.Id].Should().HaveCount(120);
            batch[light.Id].Should().HaveCount(3);

            // The whole point of #1387: one SELECT, not one per artefact (nor one per page).
            ExtractionSelects(interceptor).Should().HaveCount(1,
                "batched extraction-history loads must resolve every artefact in a single query");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByArtefactsForUserAsync_NeverReturnsAnotherUsersExtractions()
    {
        var (options, _, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var owner = AddUser(db, "extraction-owner");
            var other = AddUser(db, "extraction-other");

            var ownerArtefact = AddArtefact(db, owner.Id, "owned.txt");
            var otherArtefact = AddArtefact(db, other.Id, "foreign.txt");
            AddExtraction(db, ownerArtefact.Id, "owned", DateTimeOffset.UtcNow);
            AddExtraction(db, otherArtefact.Id, "foreign", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            var repo = new ArtefactExtractionRepository(db);

            // Request BOTH ids while scoped to the owner — the foreign artefact must be absent.
            var result = await repo.GetByArtefactsForUserAsync(
                new[] { ownerArtefact.Id, otherArtefact.Id },
                owner.Id);

            result.Should().ContainKey(ownerArtefact.Id);
            result[ownerArtefact.Id].Should().ContainSingle().Which.ExtractedText.Should().Be("owned");
            result.Should().NotContainKey(otherArtefact.Id,
                "user-scoping must exclude another user's extraction history even when its id is requested");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByArtefactsForUserAsync_OmitsArtefactsWithNoHistory_WithoutError()
    {
        var (options, _, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var user = AddUser(db, "extraction-empty-history");
            var withHistory = AddArtefact(db, user.Id, "with.txt");
            var withoutHistory = AddArtefact(db, user.Id, "without.txt");
            AddExtraction(db, withHistory.Id, "present", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            var repo = new ArtefactExtractionRepository(db);

            var result = await repo.GetByArtefactsForUserAsync(
                new[] { withHistory.Id, withoutHistory.Id },
                user.Id);

            result.Should().ContainKey(withHistory.Id);
            result.Should().NotContainKey(withoutHistory.Id,
                "an artefact with no extractions is simply absent from the map");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByArtefactsForUserAsync_WithEmptyIdSet_ReturnsEmptyAndIssuesNoQuery()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var repo = new ArtefactExtractionRepository(db);
            interceptor.Clear();

            var result = await repo.GetByArtefactsForUserAsync(Array.Empty<Guid>(), Guid.NewGuid());

            result.Should().BeEmpty();
            ExtractionSelects(interceptor).Should().BeEmpty(
                "an empty id set must short-circuit before touching the database");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByArtefactsForUserAsync_WithTooManyIds_ThrowsBeforeTouchingDatabase()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            var repo = new ArtefactExtractionRepository(db);
            var tooMany = Enumerable.Range(0, 901).Select(_ => Guid.NewGuid()).ToList();

            var act = async () => await repo.GetByArtefactsForUserAsync(tooMany, Guid.NewGuid());

            await act.Should().ThrowAsync<ArgumentException>();
            interceptor.CapturedCommands.Should().BeEmpty("the guard must trip before any SQL runs");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByArtefactsForUserAsync_WithExactlyMaxBatchIds_DoesNotThrow()
    {
        var (options, _, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var repo = new ArtefactExtractionRepository(db);
            var exactlyMax = Enumerable.Range(0, 900).Select(_ => Guid.NewGuid()).ToList();

            var result = await repo.GetByArtefactsForUserAsync(exactlyMax, Guid.NewGuid());

            result.Should().BeEmpty("no extractions were seeded; the call must succeed, not throw");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static async Task<IReadOnlyList<ArtefactExtraction>> ReadAllSequentiallyAsync(
        ArtefactExtractionRepository repo,
        Guid artefactId,
        Guid userId)
    {
        var all = new List<ArtefactExtraction>();
        while (true)
        {
            var page = await repo.GetByArtefactForUserAsync(artefactId, userId, limit: 50, offset: all.Count);
            all.AddRange(page);
            if (page.Count < 50)
                return all;
        }
    }

    private static (DbContextOptions<TaskdeckDbContext> Options, CapturingCommandInterceptor Interceptor, string DbPath) CreateSqliteOptions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-extraction-batch-{Guid.NewGuid():N}.db");
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

    private static SourceArtefact AddArtefact(TaskdeckDbContext db, Guid userId, string fileName)
    {
        var artefact = new SourceArtefact(
            userId,
            ArtefactKind.TextFile,
            "text/plain",
            fileName,
            7,
            Sha,
            CaptureSource.Import);
        db.SourceArtefacts.Add(artefact);
        return artefact;
    }

    private static void AddExtraction(TaskdeckDbContext db, Guid artefactId, string text, DateTimeOffset createdAt)
    {
        var extraction = new ArtefactExtraction(artefactId, "test-extractor", "1.0", [], text);
        typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(extraction, createdAt);
        db.ArtefactExtractions.Add(extraction);
    }

    private static IReadOnlyList<string> ExtractionSelects(CapturingCommandInterceptor interceptor) =>
        interceptor.CapturedCommands
            .Where(sql => sql.Contains("ArtefactExtractions", StringComparison.OrdinalIgnoreCase)
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
