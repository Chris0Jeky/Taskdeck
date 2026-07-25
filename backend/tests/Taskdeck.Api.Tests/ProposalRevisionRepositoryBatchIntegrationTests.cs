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
/// Integration tests for <see cref="ProposalRevisionRepository.GetByProposalIdsAsync"/> against real
/// SQLite (#1444). The service-level tests mock this repository, so nothing else proves the EF
/// translation of the batched read, its per-proposal ordering guarantee, or that its chunking never
/// loses or duplicates a row at a chunk boundary.
/// </summary>
public sealed class ProposalRevisionRepositoryBatchIntegrationTests
{
    /// <summary>
    /// Must match <c>ProposalRevisionRepository.ProposalIdChunkSize</c>. Kept as a local literal
    /// because the production constant is private; the boundary test below fails loudly if the two
    /// ever diverge, since the expected query count is derived from this value.
    /// </summary>
    private const int ChunkSize = 200;

    [Fact]
    public async Task GetByProposalIdsAsync_ResolvesEveryProposalInOneQuery_WithRevisionNumberOrderPerProposal()
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

            var batch = await repo.GetByProposalIdsAsync(new[] { first, second });

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
    public async Task GetByProposalIdsAsync_AcrossAChunkBoundary_ReturnsEveryRowExactlyOnce()
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

            var batch = await repo.GetByProposalIdsAsync(proposalIds);

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
    public async Task GetByProposalIdsAsync_WithDuplicateIds_DoesNotDuplicateRowsOrQueries()
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

            var batch = await repo.GetByProposalIdsAsync(new[] { proposalId, proposalId, proposalId });

            batch.Should().ContainSingle("repeated ids are de-duplicated, so a row cannot come back twice");
            RevisionSelects(interceptor).Should().HaveCount(1);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetByProposalIdsAsync_WithEmptyIdSet_ReturnsEmptyAndIssuesNoQuery()
    {
        var (options, interceptor, dbPath) = CreateSqliteOptions();
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var repo = new ProposalRevisionRepository(db);
            interceptor.Clear();

            var batch = await repo.GetByProposalIdsAsync(Array.Empty<Guid>());

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
    public async Task GetByProposalIdsAsync_MatchesPerProposalRead_ForTheSameProposal()
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
            var batch = await repo.GetByProposalIdsAsync(new[] { proposalId });

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
