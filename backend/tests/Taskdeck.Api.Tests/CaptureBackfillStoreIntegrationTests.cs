using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

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
            command.Parameters.Should().HaveCount(3,
                "request type, the whole excluded-id JSON array and batch size are the only parameters");
            command.Text.Should().ContainEquivalentOf("NOT IN (SELECT value FROM json_each(",
                "exclusions must be evaluated by SQLite before materialization");

            var exclusionParameter = command.Parameters.Single(parameter =>
                parameter.Value is string value && value.StartsWith("[", StringComparison.Ordinal));
            exclusionParameter.Value.Should().BeOfType<string>()
                .Which.Should().Contain(oldestExcluded.Id.ToString("D").ToUpperInvariant());

            var limitParameter = command.Parameters.Single(parameter => parameter.Value is int value && value == 1);
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

    private sealed class CapturingReaderInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<CapturedCommand> _commands = new();

        public IReadOnlyCollection<CapturedCommand> Commands => _commands;

        public void Clear() => _commands.Clear();

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
