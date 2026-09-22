using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
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
/// #1399: a bounded persistence primitive, not a claim that DataExportService has switched to it.
/// Counts actual EF materialisation before the first yield; an async wrapper around the existing
/// all-history batch method must fail even though it yields one row at a time to its consumer.
/// </summary>
public sealed class ArtefactExtractionStreamingIntegrationTests
{
    [Fact]
    public async Task Stream_MatchesSequentialBytesAndCallerOrder_AcrossTimestampTiesAndPageBoundaries()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = AddUser(fixture.Db, "owner");
        var other = AddUser(fixture.Db, "other");
        var heavy = AddArtefact(fixture.Db, owner.Id, Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var light = AddArtefact(fixture.Db, owner.Id, Guid.Parse("f0000000-0000-0000-0000-000000000002"));
        var empty = AddArtefact(fixture.Db, owner.Id, Guid.Parse("80000000-0000-0000-0000-000000000003"));
        var foreign = AddArtefact(fixture.Db, other.Id);
        // Include sub-millisecond precision and a non-zero offset. Continuation must compare the
        // stored SQLite ordering, not silently normalise timestamps or re-sort GUIDs in memory.
        var start = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(2));
        for (var i = 0; i < 123; i++)
            AddExtraction(fixture.Db, heavy.Id, $"heavy-{i}: Zoë ✅\n\"quoted\"", start.AddTicks(i / 2));
        for (var i = 0; i < 3; i++)
            AddExtraction(fixture.Db, light.Id, $"light-{i}", start);
        AddExtraction(fixture.Db, foreign.Id, "must-not-leak", start);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var requested = new[] { light.Id, empty.Id, heavy.Id, foreign.Id, light.Id };
        var expected = new List<ArtefactExtraction>();
        foreach (var id in requested.Distinct())
        {
            for (var offset = 0; ; offset += 50)
            {
                var page = await fixture.Repo.GetByArtefactForUserAsync(id, owner.Id, 50, offset);
                expected.AddRange(page);
                if (page.Count < 50) break;
            }
        }

        fixture.Counter.Reset();
        var actual = await CollectAsync(fixture.Repo.StreamByArtefactsForUserAsync(requested, owner.Id));

        actual.Should().HaveCount(126);
        actual.Select(row => row.Id).Should().Equal(expected.Select(row => row.Id));
        SerializeRows(actual).Should().Equal(SerializeRows(expected));
        actual.Should().NotContain(row => row.SourceArtefactId == foreign.Id);
        fixture.Counter.HistoryReads.Should().HaveCount(3, "126 rows require three bounded 50-row pages");
        fixture.Db.ChangeTracker.Entries<ArtefactExtraction>().Should().BeEmpty();
    }

    [Fact]
    public async Task Stream_MaterialisesOnlyOnePageBeforeYield_AndClosesReaderBeforeBackpressure()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = AddUser(fixture.Db, "bounded");
        var artefact = AddArtefact(fixture.Db, owner.Id);
        for (var i = 0; i < 123; i++)
            AddExtraction(fixture.Db, artefact.Id, $"text-{i}", DateTimeOffset.UnixEpoch.AddTicks(i));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        fixture.Counter.Reset();

        await using (var iterator = fixture.Repo.StreamByArtefactsForUserAsync([artefact.Id], owner.Id)
            .GetAsyncEnumerator())
        {
            (await iterator.MoveNextAsync()).Should().BeTrue();
            fixture.Counter.Materialised.Should().Be(50,
                "yielding one row must not hide eager loading of the whole history");
            fixture.Counter.HistoryReads.Should().HaveCount(1);
            fixture.Db.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
            // The export also copies blobs through this scoped context while the history stream is
            // paused. No active EF operation or database reader may survive a yield.
            (await fixture.Db.Users.CountAsync()).Should().Be(1);
            for (var index = 1; index < 50; index++)
                (await iterator.MoveNextAsync()).Should().BeTrue();
            fixture.Counter.Materialised.Should().Be(50);
            (await iterator.MoveNextAsync()).Should().BeTrue();
            fixture.Counter.Materialised.Should().Be(100);
            fixture.Counter.HistoryReads.Should().HaveCount(2);
        }

        fixture.Counter.Materialised.Should().Be(100, "early disposal must not drain the remaining history");
        fixture.Db.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 2)]
    [InlineData(51, 2)]
    [InlineData(100, 3)]
    public async Task Stream_HandlesEmptyAndExactPageBoundaries(int rowCount, int queryCount)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = AddUser(fixture.Db, "boundary");
        var artefact = AddArtefact(fixture.Db, owner.Id);
        for (var i = 0; i < rowCount; i++)
            AddExtraction(fixture.Db, artefact.Id, $"row-{i}", DateTimeOffset.UnixEpoch);
        await fixture.Db.SaveChangesAsync();
        fixture.Counter.Reset();

        var rows = await CollectAsync(fixture.Repo.StreamByArtefactsForUserAsync([artefact.Id], owner.Id));

        rows.Should().HaveCount(rowCount);
        rows.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        fixture.Counter.HistoryReads.Should().HaveCount(queryCount,
            "a full final page needs one empty exhaustion query, not one query per artefact");
    }

    [Fact]
    public async Task Stream_UsesOneQueryForNineHundredIdsWithNoHistory()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var ids = Enumerable.Range(0, 900).Select(_ => Guid.NewGuid()).ToArray();
        fixture.Counter.Reset();

        var rows = await CollectAsync(fixture.Repo.StreamByArtefactsForUserAsync(ids, Guid.NewGuid()));

        rows.Should().BeEmpty();
        fixture.Counter.HistoryReads.Should().HaveCount(1);
        fixture.Counter.Materialised.Should().Be(0);
    }

    [Fact]
    public async Task Stream_EmptyInputPerformsNoQuery_AndOversizedRawDuplicatesFailBeforeQuery()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        fixture.Counter.Reset();
        (await CollectAsync(fixture.Repo.StreamByArtefactsForUserAsync([], Guid.NewGuid())))
            .Should().BeEmpty();
        var duplicateIds = Enumerable.Repeat(Guid.NewGuid(), 901).ToArray();
        Action oversized = () => { _ = fixture.Repo.StreamByArtefactsForUserAsync(duplicateIds, Guid.NewGuid()); };
        oversized.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("sourceArtefactIds");
        Action nullInput = () => { _ = fixture.Repo.StreamByArtefactsForUserAsync(null!, Guid.NewGuid()); };
        nullInput.Should().Throw<ArgumentNullException>();
        fixture.Counter.HistoryReads.Should().BeEmpty();
    }

    [Fact]
    public async Task Stream_SnapshotsIdsBeforeDeferredEnumeration()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = AddUser(fixture.Db, "snapshot");
        var selected = AddArtefact(fixture.Db, owner.Id);
        var notSelected = AddArtefact(fixture.Db, owner.Id);
        AddExtraction(fixture.Db, selected.Id, "selected", DateTimeOffset.UnixEpoch);
        AddExtraction(fixture.Db, notSelected.Id, "not-selected", DateTimeOffset.UnixEpoch);
        await fixture.Db.SaveChangesAsync();

        var ids = new List<Guid> { selected.Id };
        var stream = fixture.Repo.StreamByArtefactsForUserAsync(ids, owner.Id);
        ids.Clear();
        ids.Add(notSelected.Id);
        var rows = await CollectAsync(stream);

        rows.Should().ContainSingle().Which.SourceArtefactId.Should().Be(selected.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stream_HonoursBothMethodAndEnumeratorCancellation_WithoutDraining(bool cancelMethodToken)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var owner = AddUser(fixture.Db, "cancel");
        var artefact = AddArtefact(fixture.Db, owner.Id);
        for (var i = 0; i < 51; i++)
            AddExtraction(fixture.Db, artefact.Id, $"row-{i}", DateTimeOffset.UnixEpoch);
        await fixture.Db.SaveChangesAsync();
        fixture.Counter.Reset();
        using var methodCancellation = new CancellationTokenSource();
        using var enumeratorCancellation = new CancellationTokenSource();
        await using var iterator = fixture.Repo.StreamByArtefactsForUserAsync(
                [artefact.Id], owner.Id, methodCancellation.Token)
            .GetAsyncEnumerator(enumeratorCancellation.Token);
        (await iterator.MoveNextAsync()).Should().BeTrue();
        var readsBeforeCancellation = fixture.Counter.HistoryReads.Count;
        if (cancelMethodToken) methodCancellation.Cancel();
        else enumeratorCancellation.Cancel();

        Func<Task> advance = async () => { await iterator.MoveNextAsync(); };
        await advance.Should().ThrowAsync<OperationCanceledException>();
        fixture.Counter.HistoryReads.Should().HaveCount(readsBeforeCancellation);
    }

    [Fact]
    public async Task Stream_PreCancelledMethodTokenDoesNotQuery()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Counter.Reset();
        Action start = () => { _ = fixture.Repo.StreamByArtefactsForUserAsync([Guid.NewGuid()], Guid.NewGuid(), cancellation.Token); };
        start.Should().Throw<OperationCanceledException>();
        fixture.Counter.HistoryReads.Should().BeEmpty();
    }

    private static async Task<List<ArtefactExtraction>> CollectAsync(IAsyncEnumerable<ArtefactExtraction> stream)
    {
        var rows = new List<ArtefactExtraction>();
        await foreach (var row in stream) rows.Add(row);
        return rows;
    }

    private static byte[] SerializeRows(IEnumerable<ArtefactExtraction> rows) =>
        JsonSerializer.SerializeToUtf8Bytes(rows.Select(row => new
        {
            row.Id, row.SourceArtefactId, row.ExtractorName, row.ExtractorVersion,
            row.Warnings, row.ExtractedText, row.TextLength, row.CreatedAt
        }));

    private static User AddUser(TaskdeckDbContext db, string name)
    {
        var user = new User(name, $"{name}@example.com", "hash");
        db.Users.Add(user);
        return user;
    }

    private static SourceArtefact AddArtefact(TaskdeckDbContext db, Guid userId, Guid? id = null)
    {
        var artefact = new SourceArtefact(userId, ArtefactKind.TextFile, "text/plain", "fixture.txt",
            1, new string('a', 64), CaptureSource.Import);
        if (id.HasValue) typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(artefact, id.Value);
        db.SourceArtefacts.Add(artefact);
        return artefact;
    }

    private static void AddExtraction(TaskdeckDbContext db, Guid artefactId, string text, DateTimeOffset createdAt)
    {
        var extraction = new ArtefactExtraction(artefactId, "fixture", "1.0", ["fixture-warning"], text);
        typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(extraction, createdAt);
        db.ArtefactExtractions.Add(extraction);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"taskdeck-extraction-stream-{Guid.NewGuid():N}.db");
        public ReadCounter Counter { get; } = new();
        public TaskdeckDbContext Db { get; }
        public ArtefactExtractionRepository Repo { get; }

        private SqliteFixture()
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(_path))
                .AddInterceptors(Counter, MaterialisationObserver.Instance).Options;
            Db = new TaskdeckDbContext(options);
            MaterialisationObserver.Instance.Register(Db, Counter);
            Repo = new ArtefactExtractionRepository(Db);
        }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var fixture = new SqliteFixture();
            try { await fixture.Db.Database.MigrateAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".migrate.lock" })
            {
                try { File.Delete(_path + suffix); }
                catch (IOException) { /* best-effort cleanup of this fixture's own files */ }
            }
        }
    }

    // IMaterializationInterceptor is an EF singleton service. Reuse one observer rather than
    // creating a new internal service provider per fixture; route counts by weak context identity.
    private sealed class MaterialisationObserver : IMaterializationInterceptor
    {
        public static MaterialisationObserver Instance { get; } = new();
        private readonly ConditionalWeakTable<DbContext, ReadCounter> _counters = new();
        public void Register(DbContext context, ReadCounter counter) => _counters.Add(context, counter);
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (entity is ArtefactExtraction && _counters.TryGetValue(data.Context, out var counter))
                counter.RecordMaterialised();
            return entity;
        }
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();
        private int _materialised;
        public int Materialised => Volatile.Read(ref _materialised);
        public IReadOnlyList<string> HistoryReads => _commands.Where(sql =>
            sql.Contains("ArtefactExtractions", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase)).ToArray();
        public void Reset() { _commands.Clear(); Interlocked.Exchange(ref _materialised, 0); }
        public void RecordMaterialised() => Interlocked.Increment(ref _materialised);
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
