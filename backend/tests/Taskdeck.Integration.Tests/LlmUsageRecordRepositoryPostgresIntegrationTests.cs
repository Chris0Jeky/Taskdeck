using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Repositories;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

/// <summary>
/// Exercises the non-SQLite finalization branches of <see cref="LlmUsageRecordRepository"/> (issue
/// #1313) against a real Npgsql-backed context. On a non-SQLite provider the reserve/commit/release
/// paths run through EF (not the raw SQLite SQL, whose unquoted PascalCase identifiers Npgsql folds to
/// lowercase and cannot match), so a reservation made here must also commit and release through EF.
/// </summary>
[Collection(PostgresTestCollection.Name)]
public class LlmUsageRecordRepositoryPostgresIntegrationTests : PostgresIntegrationTestBase
{
    public LlmUsageRecordRepositoryPostgresIntegrationTests(PostgresContainerFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task CommitReservation_NonSqlite_TurnsReservationIntoCommittedUsage()
    {
        SkipIfDockerUnavailable();
        var repo = new LlmUsageRecordRepository(Db);
        var userId = Guid.NewGuid();

        var reservationId = await ReserveSlotAsync(repo, userId);

        var result = await repo.CommitReservationAsync(
            reservationId, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 123, 45);
        result.Should().Be(QuotaCommitResult.Committed);

        Db.ChangeTracker.Clear();
        var row = await Db.LlmUsageRecords.SingleAsync(r => r.Id == reservationId);
        row.Status.Should().Be(LlmUsageRecordStatus.Committed);
        row.ExpiresAt.Should().BeNull();
        row.Provider.Should().Be("OpenAI");
        row.Model.Should().Be("gpt-4");
        row.InputTokens.Should().Be(123);
        row.OutputTokens.Should().Be(45);
    }

    [SkippableFact]
    public async Task CommitReservation_NonSqlite_DoubleCommitIsIdempotent()
    {
        SkipIfDockerUnavailable();
        var repo = new LlmUsageRecordRepository(Db);
        var userId = Guid.NewGuid();

        var reservationId = await ReserveSlotAsync(repo, userId);

        var first = await repo.CommitReservationAsync(
            reservationId, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 123, 45);
        first.Should().Be(QuotaCommitResult.Committed);

        var second = await repo.CommitReservationAsync(
            reservationId, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 999, 999);
        second.Should().Be(QuotaCommitResult.AlreadySettled);

        Db.ChangeTracker.Clear();
        var row = await Db.LlmUsageRecords.SingleAsync(r => r.Id == reservationId);
        row.InputTokens.Should().Be(123, "the already-settled duplicate must not overwrite committed usage");
        row.OutputTokens.Should().Be(45);
    }

    [SkippableFact]
    public async Task CommitReservation_NonSqlite_AfterRowSwept_RecoversBilledUsage()
    {
        SkipIfDockerUnavailable();
        var repo = new LlmUsageRecordRepository(Db);
        var userId = Guid.NewGuid();

        var reservationId = await ReserveSlotAsync(repo, userId);

        // Simulate the TTL sweep deleting the reservation row while the slow LLM call was in flight.
        var reservedRow = await Db.LlmUsageRecords.SingleAsync(r => r.Id == reservationId);
        Db.LlmUsageRecords.Remove(reservedRow);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var result = await repo.CommitReservationAsync(
            reservationId, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 321, 12);
        result.Should().Be(QuotaCommitResult.RecoveredExpired);

        Db.ChangeTracker.Clear();
        var row = await Db.LlmUsageRecords.SingleAsync(r => r.Id == reservationId);
        row.Status.Should().Be(LlmUsageRecordStatus.Committed);
        row.ExpiresAt.Should().BeNull();
        row.UserId.Should().Be(userId);
        row.InputTokens.Should().Be(321);
        row.OutputTokens.Should().Be(12);

        // A late/duplicate commit of the recovered id stays idempotent (no second row, no double-count).
        var dup = await repo.CommitReservationAsync(
            reservationId, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 321, 12);
        dup.Should().Be(QuotaCommitResult.AlreadySettled);

        Db.ChangeTracker.Clear();
        (await Db.LlmUsageRecords.CountAsync(r => r.Id == reservationId)).Should().Be(1);
    }

    [SkippableFact]
    public async Task ReleaseReservation_NonSqlite_FreesSlotThenNoOp()
    {
        SkipIfDockerUnavailable();
        var repo = new LlmUsageRecordRepository(Db);
        var userId = Guid.NewGuid();

        var reservationId = await ReserveSlotAsync(repo, userId);

        var released = await repo.ReleaseReservationAsync(reservationId);
        released.Should().BeTrue();

        Db.ChangeTracker.Clear();
        (await Db.LlmUsageRecords.CountAsync(r => r.Id == reservationId)).Should().Be(0);

        var again = await repo.ReleaseReservationAsync(reservationId);
        again.Should().BeFalse("a second release of an already-freed reservation is a no-op");
    }

    [SkippableFact]
    public async Task CommitReservation_NonSqlite_RowSweptWhileEntityStillTracked_RecoversWithoutThrowing()
    {
        SkipIfDockerUnavailable();
        var repo = new LlmUsageRecordRepository(Db);
        var userId = Guid.NewGuid();

        // Do NOT clear the tracker: in the service flow the reservation entity created by the reserve
        // fallback stays tracked (Unchanged) in the shared scoped context. Delete the row out-of-band
        // (raw SQL bypasses the tracker) so the tracked instance survives while the DB row is gone —
        // the identity-map state the recovery insert must cope with.
        var reservationId = await ReserveSlotAsync(repo, userId);
        await Db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"LlmUsageRecords\" WHERE \"Id\" = {0}", reservationId);

        var result = await repo.CommitReservationAsync(
            reservationId, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 321, 12);
        result.Should().Be(QuotaCommitResult.RecoveredExpired,
            "a swept reservation must be recovered even while the stale entity is still tracked");

        Db.ChangeTracker.Clear();
        var row = await Db.LlmUsageRecords.SingleAsync(r => r.Id == reservationId);
        row.Status.Should().Be(LlmUsageRecordStatus.Committed);
        row.ExpiresAt.Should().BeNull();
        row.UserId.Should().Be(userId);
        row.InputTokens.Should().Be(321);
        row.OutputTokens.Should().Be(12);
    }

    [SkippableFact]
    public async Task ReleaseReservation_NonSqlite_RowDeletedOutOfBandWhileTracked_ReturnsFalseWithoutThrowing()
    {
        SkipIfDockerUnavailable();
        var repo = new LlmUsageRecordRepository(Db);
        var userId = Guid.NewGuid();

        // Same tracker-vs-database divergence as the recovery test: the reservation entity stays
        // tracked while the row is deleted out-of-band (e.g. a concurrent sweep).
        var reservationId = await ReserveSlotAsync(repo, userId);
        await Db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"LlmUsageRecords\" WHERE \"Id\" = {0}", reservationId);

        var released = await repo.ReleaseReservationAsync(reservationId);
        released.Should().BeFalse("releasing an externally-deleted reservation is a no-op, not a fault");
    }

    [SkippableFact]
    public async Task CommitReservation_NonSqlite_RecoveryInsertFailsForNonDuplicateReason_Rethrows()
    {
        SkipIfDockerUnavailable();
        var repo = new LlmUsageRecordRepository(Db);
        var userId = Guid.NewGuid();

        var reservationId = await ReserveSlotAsync(repo, userId);
        await Db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"LlmUsageRecords\" WHERE \"Id\" = {0}", reservationId);

        // Provider exceeds its 100-char column limit, so the recovery insert fails with a real write
        // error — not the duplicate-key settle race. It must propagate instead of being silently
        // reported as AlreadySettled (which would drop billed usage).
        var oversizedProvider = new string('p', 150);

        await FluentActions
            .Awaiting(() => repo.CommitReservationAsync(
                reservationId, userId, LlmSurface.Chat, oversizedProvider, "gpt-4", 1, 1))
            .Should().ThrowAsync<DbUpdateException>();

        Db.ChangeTracker.Clear();
        (await Db.LlmUsageRecords.CountAsync(r => r.Id == reservationId)).Should().Be(0,
            "a failed recovery insert must surface, leaving no row, rather than claim settlement");
    }

    /// <summary>
    /// Reserves a single quota slot (unlimited token budgets, generous request budget) via the
    /// non-SQLite reserve fallback and returns the reservation id.
    /// </summary>
    private async Task<Guid> ReserveSlotAsync(LlmUsageRecordRepository repo, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var outcome = await repo.TryReserveAsync(
            userId,
            LlmSurface.Chat,
            hourStart: now.AddHours(-1),
            now: now,
            dayStart: now.AddDays(-1),
            dayEnd: now.AddDays(1),
            requestsPerHour: 10,
            tokensPerDay: 1_000_000,
            globalBudgetCeilingTokens: 0,
            estimatedTokens: 500,
            expiresAt: now.AddSeconds(120));

        outcome.Decision.Should().Be(QuotaReservationDecision.Allowed);
        outcome.ReservationId.Should().NotBeNull();
        return outcome.ReservationId!.Value;
    }
}
