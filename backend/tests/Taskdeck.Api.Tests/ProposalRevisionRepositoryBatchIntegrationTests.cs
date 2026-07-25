using System.Collections.Concurrent;
using System.Data.Common;
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
/// Integration tests for the two-phase batched revision read against real SQLite (#1444):
/// <see cref="ProposalRevisionRepository.GetRefsByProposalIdsAsync"/> (payload-free metadata for a
/// page) and <see cref="ProposalRevisionRepository.GetByIdsAsync"/> (payloads for the winners only).
/// The service-level tests mock this repository, so nothing else proves the EF translation, the
/// per-proposal ordering guarantee, that the ref projection really does not read the payload, or that
/// chunking never loses or duplicates a row at a boundary.
/// </summary>
public sealed class ProposalRevisionRepositoryBatchIntegrationTests
{
    /// <summary>
    /// Must match <c>ProposalRevisionRepository.IdChunkSize</c>. Kept as a local literal because the
    /// production constant is private; the boundary tests below fail loudly if the two ever diverge,
    /// since their expected query counts are derived from this value (a chunk size of 300 would give
    /// 1 query where 2 is expected, and 100 would give 3).
    /// </summary>
    private const int ChunkSize = 200;

    [Fact]
    public async Task GetRefsByProposalIdsAsync_ResolvesEveryProposalInOneQuery_WithRevisionNumberOrderPerProposal()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var first = AddProposal(db);
            var second = AddProposal(db);
            var unrequested = AddProposal(db);
            // Insert out of revision-number order so an "insertion order happens to be right" pass
            // cannot masquerade as the ordering guarantee.
            AddRevision(db, first, 3);
            AddRevision(db, second, 2);
            AddRevision(db, first, 1);
            AddRevision(db, first, 2);
            AddRevision(db, second, 1);
            AddRevision(db, unrequested, 1);
            await db.SaveChangesAsync();

            var repo = new ProposalRevisionRepository(db);
            interceptor.Clear();

            var batch = await repo.GetRefsByProposalIdsAsync(new[] { first, second });

            batch.Where(r => r.ProposalId == first).Select(r => r.RevisionNumber).Should().Equal(1, 2, 3);
            batch.Where(r => r.ProposalId == second).Select(r => r.RevisionNumber).Should().Equal(1, 2);
            batch.Should().NotContain(r => r.ProposalId == unrequested,
                "only revisions of the requested proposals may be returned");
            batch.Should().HaveCount(5);

            RevisionSelects(interceptor).Should().HaveCount(1,
                "the whole point of the batch read is one query for the page, not one per proposal");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetRefsByProposalIdsAsync_AcrossAChunkBoundary_ReturnsEveryRowExactlyOnce()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            // One more proposal than a single chunk holds, each with two revisions, so a chunking bug
            // that drops or double-counts the boundary row is visible in the totals.
            var proposalIds = Enumerable.Range(0, ChunkSize + 1).Select(_ => AddProposal(db)).ToList();
            foreach (var proposalId in proposalIds)
            {
                AddRevision(db, proposalId, 1);
                AddRevision(db, proposalId, 2);
            }
            await db.SaveChangesAsync();

            var repo = new ProposalRevisionRepository(db);
            interceptor.Clear();

            var batch = await repo.GetRefsByProposalIdsAsync(proposalIds);

            batch.Should().HaveCount((ChunkSize + 1) * 2, "no row may be lost or duplicated across chunks");
            batch.Select(r => r.Id).Should().OnlyHaveUniqueItems();
            batch.Select(r => r.ProposalId).Distinct().Should().HaveCount(ChunkSize + 1);
            foreach (var proposalId in proposalIds)
            {
                batch.Where(r => r.ProposalId == proposalId).Select(r => r.RevisionNumber).Should()
                    .Equal(new[] { 1, 2 }, "each proposal's revisions stay in ascending revision-number order");
            }

            RevisionSelects(interceptor).Should().HaveCount(2,
                $"{ChunkSize + 1} ids at a chunk size of {ChunkSize} must cost exactly two queries — "
                + "still O(chunks), never O(proposals)");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetRefsByProposalIdsAsync_AtExactlyOneChunk_IssuesExactlyOneQuery()
    {
        // #1444 review: only chunk+1 was covered, so an off-by-one to `offset <= ids.Count` still gave
        // 2 queries for 201 ids and passed — while every request whose id count is an exact multiple of
        // the chunk size issued a wasted trailing `IN ()` query. Pin the exact-multiple case.
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var proposalIds = Enumerable.Range(0, ChunkSize).Select(_ => AddProposal(db)).ToList();
            foreach (var proposalId in proposalIds)
                AddRevision(db, proposalId, 1);
            await db.SaveChangesAsync();

            var repo = new ProposalRevisionRepository(db);
            interceptor.Clear();

            var batch = await repo.GetRefsByProposalIdsAsync(proposalIds);

            batch.Should().HaveCount(ChunkSize);
            RevisionSelects(interceptor).Should().HaveCount(1,
                $"exactly {ChunkSize} ids fill one chunk exactly and must not trigger a trailing empty query");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByIdsAsync_LoadsOnlyTheRequestedRevisions_WithTheirPayloads()
    {
        // Phase 2 of the two-phase read: once the refs decide which revisions win, only those are
        // loaded — and unlike the refs, these carry the payload the DTO builder needs.
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var proposalId = AddProposal(db);
            AddRevision(db, proposalId, 1);
            AddRevision(db, proposalId, 2);
            AddRevision(db, proposalId, 3);
            await db.SaveChangesAsync();

            var repo = new ProposalRevisionRepository(db);
            var all = await repo.GetByProposalIdAsync(proposalId);
            var wanted = all.Single(r => r.RevisionNumber == 2);
            interceptor.Clear();

            var loaded = await repo.GetByIdsAsync(new[] { wanted.Id, wanted.Id });

            loaded.Should().ContainSingle("repeated ids are de-duplicated");
            loaded[0].Id.Should().Be(wanted.Id);
            loaded[0].RevisedPayload.Should().Be(wanted.RevisedPayload,
                "phase 2 exists precisely to carry the payload the ref projection omits");
            RevisionSelects(interceptor).Should().HaveCount(1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByIdsAsync_WithEmptyIdSet_ReturnsEmptyAndIssuesNoQuery()
    {
        // The list read skips phase 2 entirely when no revision won, so the empty short-circuit is on a
        // real code path, not a defensive nicety (#1444 review).
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var repo = new ProposalRevisionRepository(db);
            interceptor.Clear();

            var loaded = await repo.GetByIdsAsync(Array.Empty<Guid>());

            loaded.Should().BeEmpty();
            RevisionSelects(interceptor).Should().BeEmpty();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByIdsAsync_AcrossAChunkBoundary_ReturnsEveryRequestedRowExactlyOnce()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var proposalIds = Enumerable.Range(0, ChunkSize + 1).Select(_ => AddProposal(db)).ToList();
            foreach (var proposalId in proposalIds)
                AddRevision(db, proposalId, 1);
            await db.SaveChangesAsync();

            var repo = new ProposalRevisionRepository(db);
            var revisionIds = (await repo.GetRefsByProposalIdsAsync(proposalIds)).Select(r => r.Id).ToList();
            revisionIds.Should().HaveCount(ChunkSize + 1);
            interceptor.Clear();

            var loaded = await repo.GetByIdsAsync(revisionIds);

            loaded.Should().HaveCount(ChunkSize + 1, "no row may be lost or duplicated across chunks");
            loaded.Select(r => r.Id).Should().OnlyHaveUniqueItems();
            RevisionSelects(interceptor).Should().HaveCount(2);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetRefsByProposalIdsAsync_DoesNotSelectTheRevisionPayload()
    {
        // The whole reason phase 1 exists (#1444 review): RevisedPayload is unbounded, so a page read
        // must not pull it for revisions that will lose. The ref type has no payload member, so this
        // asserts the generated SQL itself never mentions the column.
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var proposalId = AddProposal(db);
            AddRevision(db, proposalId, 1);
            await db.SaveChangesAsync();

            var repo = new ProposalRevisionRepository(db);
            interceptor.Clear();

            await repo.GetRefsByProposalIdsAsync(new[] { proposalId });

            var sql = RevisionSelects(interceptor).Should().ContainSingle().Which;
            sql.Should().NotContain("RevisedPayload",
                "the ref projection must not read the unbounded payload column");
            sql.Should().Contain("RevisionNumber", "but it must read the columns the selector compares");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetRefsByProposalIdsAsync_WithDuplicateIds_DoesNotDuplicateRowsOrQueries()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var proposalId = AddProposal(db);
            AddRevision(db, proposalId, 1);
            await db.SaveChangesAsync();

            var repo = new ProposalRevisionRepository(db);
            interceptor.Clear();

            var batch = await repo.GetRefsByProposalIdsAsync(new[] { proposalId, proposalId, proposalId });

            batch.Should().ContainSingle("repeated ids are de-duplicated, so a row cannot come back twice");
            RevisionSelects(interceptor).Should().HaveCount(1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetRefsByProposalIdsAsync_WithEmptyIdSet_ReturnsEmptyAndIssuesNoQuery()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var repo = new ProposalRevisionRepository(db);
            interceptor.Clear();

            var batch = await repo.GetRefsByProposalIdsAsync(Array.Empty<Guid>());

            batch.Should().BeEmpty();
            RevisionSelects(interceptor).Should().BeEmpty(
                "an empty id set must short-circuit before touching the database");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetRefsByProposalIdsAsync_MatchesPerProposalRead_ForTheSameProposal()
    {
        // Parity with the single-proposal read this method batches: the effective-revision selector is
        // shared between the list and single paths, so the two repository reads must agree on content.
        var (options, _, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();

            var proposalId = AddProposal(db);
            AddRevision(db, proposalId, 2);
            AddRevision(db, proposalId, 1);
            AddRevision(db, proposalId, 3);
            await db.SaveChangesAsync();

            var repo = new ProposalRevisionRepository(db);

            var single = await repo.GetByProposalIdAsync(proposalId);
            var batch = await repo.GetRefsByProposalIdsAsync(new[] { proposalId });

            batch.Select(r => r.Id).Should().Equal(single.Select(r => r.Id),
                "the batched read must return the same revisions in the same per-proposal order");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (DbContextOptions<TaskdeckDbContext> Options, CapturingCommandInterceptor Interceptor, string DbPath) CreateSqliteOptions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-revision-batch-{Guid.NewGuid():N}.db");
        var interceptor = new CapturingCommandInterceptor();
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .AddInterceptors(interceptor)
            .Options;
        return (options, interceptor, dbPath);
    }

    /// <summary>
    /// Inserts a parent proposal and returns its id. ProposalRevisions carries a cascade FK to
    /// AutomationProposals, so a revision cannot be seeded against a synthetic proposal id.
    /// </summary>
    private static Guid AddProposal(TaskdeckDbContext db)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "batch read fixture",
            RiskLevel.Low,
            Guid.NewGuid().ToString());
        db.AutomationProposals.Add(proposal);
        return proposal.Id;
    }

    private static void AddRevision(TaskdeckDbContext db, Guid proposalId, int revisionNumber)
    {
        db.ProposalRevisions.Add(new ProposalRevision(
            proposalId,
            revisionNumber,
            Guid.NewGuid(),
            """{"operations":[]}""",
            $"revision {revisionNumber}"));
    }

    private static IReadOnlyList<string> RevisionSelects(CapturingCommandInterceptor interceptor) =>
        interceptor.CapturedCommands
            .Where(sql => sql.Contains("ProposalRevisions", StringComparison.OrdinalIgnoreCase)
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
    /// actually reached SQLite (proving one query per chunk, not one per proposal). Mirrors the
    /// interceptor in <see cref="ArtefactExtractionRepositoryBatchIntegrationTests"/>.
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
