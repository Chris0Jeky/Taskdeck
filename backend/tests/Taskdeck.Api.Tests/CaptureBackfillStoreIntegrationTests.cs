using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;
using Capture = Taskdeck.Domain.Entities.Capture;

namespace Taskdeck.Api.Tests;

/// <summary>Real-SQL regressions for the bounded CF-01 reconcile backlog (#2347).</summary>
public sealed class CaptureBackfillStoreIntegrationTests
{
    [Fact]
    public async Task GetLegacyCaptureBacklogAsync_ShouldExcludeServerSideAndLimitMaterializationToBatchSize()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-capture-backfill-{Guid.NewGuid():N}.db");
        var interceptor = new CapturingReaderInterceptor();
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(dbPath))
            .AddInterceptors(interceptor)
            .Options;

        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var user = new User("capture-backfill-bound", "capture-backfill-bound@example.com", "hash");
            db.Users.Add(user);

            var oldestExcluded = AddQueueRow(db, user.Id, "excluded oldest", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
            var nextExcluded = AddQueueRow(db, user.Id, "excluded next", new DateTimeOffset(2026, 9, 1, 0, 1, 0, TimeSpan.Zero));
            var healthy = AddQueueRow(db, user.Id, "healthy", new DateTimeOffset(2026, 9, 1, 0, 2, 0, TimeSpan.Zero));
            await db.SaveChangesAsync();

            // More than SQLite's traditional variable ceiling. A parameter-per-id implementation
            // would fail, while one json_each collection parameter stays bounded.
            var excluded = Enumerable.Range(0, 1_100)
                .Select(_ => Guid.NewGuid())
                .Append(oldestExcluded.Id)
                .Append(nextExcluded.Id)
                .ToHashSet();
            interceptor.Clear();

            var rows = await new EfCaptureBackfillStore(db)
                .GetLegacyCaptureBacklogAsync(batchSize: 1, excluded);

            rows.Should().ContainSingle().Which.Id.Should().Be(healthy.Id,
                "excluded head rows must not consume the database limit");
            var command = interceptor.Commands.Single(
                captured => captured.Text.Contains("FROM LlmRequests", StringComparison.OrdinalIgnoreCase));
            command.Parameters.Should().HaveCount(4,
                "request type, repair version, excluded-id JSON array and batch size are the only parameters");
            command.Text.Should().ContainEquivalentOf("NOT IN (SELECT value FROM json_each(",
                "exclusions must be evaluated by SQLite before materialization");

            var exclusionParameter = command.Parameters.Single(parameter =>
                parameter.Value is string value && value.StartsWith("[", StringComparison.Ordinal));
            exclusionParameter.Value.Should().BeOfType<string>()
                .Which.Should().Contain(oldestExcluded.Id.ToString("D").ToUpperInvariant());

            var limitParameter = command.Parameters.Last(parameter => parameter.Value is int value && value == 1);
            var limitToken = limitParameter.Name.StartsWith('@')
                ? limitParameter.Name
                : $"@{limitParameter.Name}";
            command.Text.Should().Contain($"LIMIT {limitToken}",
                "the SQL limit must be exactly batchSize, independent of excluded count");
            command.Parameters
                .Where(parameter => parameter.Value is int value && value == excluded.Count + 1)
                .Should().BeEmpty();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task UpgradeRepair_ShouldFindMaskedRowsPersistProgressAndNeverTrustTheOldMarker()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-capture-upgrade-{Guid.NewGuid():N}.db");
        var interceptor = new CapturingReaderInterceptor();
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(dbPath)).AddInterceptors(interceptor).Options;
        try
        {
            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            var user = new User("capture-upgrade", "capture-upgrade@example.com", "hash");
            db.Users.Add(user);
            var ids = new List<Guid>();
            for (var index = 0; index < 4; index++)
            {
                var row = AddQueueRow(db, user.Id, "original", DateTimeOffset.UtcNow.AddDays(-4 + index));
                var capture = CaptureIntakeService.BuildCapture(row,
                    CaptureRequestContract.ParseStoredPayload(row.Payload), user.Id, null);
                typeof(Capture).GetProperty(nameof(Capture.LegacyReconciliationVersion))!.SetValue(capture, 0);
                row.UpdatePayload(CaptureRequestContract.SerializePayload(
                    CaptureRequestContract.ParseStoredPayload(row.Payload) with { Text = "corrected" }));
                if (index == 0) capture.Archive();
                else capture.Keep();
                capture.UpdatedAt.Should().BeOnOrAfter(row.UpdatedAt);
                db.Captures.Add(capture);
                ids.Add(row.Id);
            }
            var oldMarker = new CaptureBackfillState(
                new Guid("2f5c9d41-7f0e-4a63-9d8a-1c6b4f2a9e55"), "capture.legacy-queue.v1", DateTimeOffset.UtcNow);
            oldMarker.MarkComplete(DateTimeOffset.UtcNow);
            db.CaptureBackfillStates.Add(oldMarker);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var store = new EfCaptureBackfillStore(db);
            (await store.CountLegacyCaptureBacklogAsync()).Should().Be(4);
            interceptor.Clear();
            var page = await store.GetLegacyCaptureBacklogAsync(1, new[] { ids[0] });
            page.Should().ContainSingle().Which.Id.Should().Be(ids[1]);
            interceptor.MaterializedRequests.Should().Be(1, "SQLite must exclude and limit before loading payloads");

            var unit = new Mock<IUnitOfWork>();
            unit.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => db.SaveChangesAsync(token));
            var service = new CaptureBackfillService(unit.Object, new EfCaptureStore(db), store);
            var result = await service.RunAsync(batchSize: 1);
            result.Reconciled.Should().Be(3);
            result.Skipped.Should().Be(1);
            result.Remaining.Should().Be(1);
            result.Complete.Should().BeFalse();
            (await store.GetStateAsync(CaptureBackfillState.LegacyQueueBackfillKey))!.IsComplete.Should().BeFalse();

            db.ChangeTracker.Clear();
            var healthy = await new EfCaptureStore(db).GetByIdForUserAsync(ids[1], user.Id);
            healthy!.CurrentText.Should().Be("corrected");
            healthy.SourceAssets.Should().HaveCount(2);
            healthy.SourceAssets.Single(asset => !asset.IsActive).TextPayload!.Text.Should().Be("original");
            healthy.LegacyReconciliationVersion.Should().Be(Capture.CurrentLegacyReconciliationVersion);
            interceptor.Clear();
            var retry = await service.RunAsync(batchSize: 1);
            retry.Reconciled.Should().Be(0);
            retry.Remaining.Should().Be(1);
            interceptor.MaterializedRequests.Should().Be(1, "successful rows must not rescan payloads on restart");
        }
        finally { Cleanup(dbPath); }
    }

    private static LlmRequest AddQueueRow(
        TaskdeckDbContext db,
        Guid userId,
        string text,
        DateTimeOffset createdAt)
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            text);
        var request = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(payload));
        typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(request, createdAt);
        typeof(Entity).GetProperty(nameof(Entity.UpdatedAt))!.SetValue(request, createdAt);
        db.LlmRequests.Add(request);
        return request;
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = dbPath + suffix;
            if (!File.Exists(path))
            {
                continue;
            }

            try { File.Delete(path); }
            catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    private sealed record CapturedCommand(
        string Text,
        IReadOnlyList<CapturedParameter> Parameters);

    private sealed record CapturedParameter(string Name, object Value);

    private sealed class CapturingReaderInterceptor : DbCommandInterceptor, IMaterializationInterceptor
    {
        private readonly ConcurrentQueue<CapturedCommand> _commands = new();

        public IReadOnlyCollection<CapturedCommand> Commands => _commands;

        public int MaterializedRequests { get; private set; }

        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (entity is LlmRequest) MaterializedRequests++;
            return entity;
        }

        public void Clear()
        {
            _commands.Clear();
            MaterializedRequests = 0;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Capture(DbCommand command) =>
            _commands.Enqueue(new CapturedCommand(
                command.CommandText,
                command.Parameters.Cast<DbParameter>()
                    .Select(parameter => new CapturedParameter(
                        parameter.ParameterName,
                        parameter.Value ?? DBNull.Value))
                    .ToList()));
    }
}
