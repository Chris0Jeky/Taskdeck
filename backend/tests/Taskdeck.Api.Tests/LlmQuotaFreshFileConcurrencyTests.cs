using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Exercises #1435 without WebApplicationFactory or a persistent keep-alive connection.
/// Setup closes before the first reservation burst, and every contender uses the same known file.
/// These are same-process, independent-connection tests, not a cross-process qualification.
/// </summary>
public sealed class LlmQuotaFreshFileConcurrencyTests
{
    [Theory]
    [InlineData(1L, 0L, 0L, false, QuotaReservationDecision.RequestsExceeded)]
    [InlineData(0L, 500L, 0L, false, QuotaReservationDecision.TokensExceeded)]
    [InlineData(0L, 0L, 500L, true, QuotaReservationDecision.GlobalExceeded)]
    public async Task ReserveAsync_FirstBurstOnFreshFile_AdmitsOneAndPersistsOne(
        long requestsPerHour,
        long tokensPerDay,
        long globalBudget,
        bool distinctUsers,
        QuotaReservationDecision deniedDecision)
    {
        const int racers = 4;
        const int estimate = 500;
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        for (var iteration = 0; iteration < 6; iteration++)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-quota-cold-{Guid.NewGuid():N}.db");
            var builder = new DbContextOptionsBuilder<TaskdeckDbContext>();
            builder.UseTaskdeckSqlite(TestSqlite.ConnectionString(dbPath), new DatabaseSettings());
            var options = builder.Options;
            try
            {
                // Use the real migrations and production pragma configuration. Pooling is off;
                // disposing setup leaves no connection alive to warm/hold WAL shared memory.
                await using (var setup = new TaskdeckDbContext(options))
                {
                    await setup.Database.MigrateAsync();
                    await setup.Database.OpenConnectionAsync();
                    await using var command = setup.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "PRAGMA journal_mode;";
                    (await command.ExecuteScalarAsync())!.ToString().Should().Be("wal");
                }

                var sharedUser = Guid.NewGuid();
                using var barrier = new Barrier(racers);
                var tasks = Enumerable.Range(0, racers).Select(_ => Task.Factory.StartNew(async () =>
                {
                    await using var db = new TaskdeckDbContext(options);
                    db.Database.GetDbConnection().DataSource.Should().Be(dbPath);
                    var repo = new LlmUsageRecordRepository(db);
                    var userId = distinctUsers ? Guid.NewGuid() : sharedUser;

                    // Dedicated contenders avoid thread-pool starvation at this blocking barrier.
                    // No contender has opened its database connection before the rendezvous.
                    barrier.SignalAndWait(TimeSpan.FromSeconds(30)).Should().BeTrue();
                    return await repo.TryReserveAsync(
                        userId, LlmSurface.Chat, now.AddHours(-1), now,
                        dayStart, dayStart.AddDays(1), requestsPerHour, tokensPerDay,
                        globalBudget, estimate, now.AddMinutes(2));
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                    .Unwrap()).ToArray();

                var results = await Task.WhenAll(tasks);
                var winner = results.Should().ContainSingle(
                    result => result.Decision == QuotaReservationDecision.Allowed,
                    $"fresh-file burst {iteration} has exactly one slot").Subject;
                winner.ReservationId.Should().NotBeNull();
                results.Where(result => result.Decision != QuotaReservationDecision.Allowed)
                    .Should().HaveCount(racers - 1)
                    .And.OnlyContain(result => result.Decision == deniedDecision && result.ReservationId == null);

                // Returned success alone is insufficient: independently reopen the file and prove
                // that all contenders collectively inserted exactly the single winning reservation.
                await using var verify = new TaskdeckDbContext(options);
                var rows = await verify.LlmUsageRecords.AsNoTracking().ToListAsync();
                var row = rows.Should().ContainSingle().Subject;
                row.Id.Should().Be(winner.ReservationId!.Value);
                row.Status.Should().Be(LlmUsageRecordStatus.Reserved);
                row.InputTokens.Should().Be(estimate);
                row.OutputTokens.Should().Be(0);
            }
            finally
            {
                foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".migrate.lock" })
                {
                    try { File.Delete(dbPath + suffix); }
                    catch (IOException) { /* best-effort cleanup of this test's own files only */ }
                }
            }
        }
    }
}
