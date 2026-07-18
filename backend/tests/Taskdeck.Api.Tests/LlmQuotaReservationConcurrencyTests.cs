using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Factory pinning RequestsPerHour = 1 so the atomic-reservation boundary (issue #1313) sits at a
/// single slot: with zero prior usage exactly one of two concurrent reservers may pass.
/// </summary>
public class SingleSlotQuotaWebApplicationFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LlmQuotaSettings>();
            services.AddSingleton(new LlmQuotaSettings
            {
                RequestsPerHour = 1,
                TokensPerDay = 1_000_000,
                GlobalBudgetCeilingTokens = 0,
                ReservationEstimatedTokens = 500,
                ReservationTtlSeconds = 120
            });
        });
    }
}

/// <summary>
/// Proves the check-then-record TOCTOU (issue #1313) is closed: the reservation makes check + insert
/// atomic, so two boundary-crossers serialize; the failure path releases without leaking quota; and a
/// stale (crashed-process) reservation expires by age instead of permanently consuming a slot.
/// </summary>
public class LlmQuotaReservationConcurrencyTests : IClassFixture<SingleSlotQuotaWebApplicationFactory>
{
    private readonly SingleSlotQuotaWebApplicationFactory _factory;

    public LlmQuotaReservationConcurrencyTests(SingleSlotQuotaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReserveAsync_TwoConcurrentAtBoundary_ExactlyOnePasses()
    {
        var userId = Guid.NewGuid();

        // Two independent DI scopes → two DbContexts → two SQLite connections contending on one file.
        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var quotaA = scopeA.ServiceProvider.GetRequiredService<ILlmQuotaService>();
        var quotaB = scopeB.ServiceProvider.GetRequiredService<ILlmQuotaService>();

        // A barrier forces both attempts to fire together (no sleeps); SQLite's single-writer lock over
        // the conditional INSERT ... SELECT ... WHERE is what actually serializes them so exactly one
        // crosses the single-slot boundary.
        using var barrier = new Barrier(2);

        var taskA = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await quotaA.ReserveAsync(userId, LlmSurface.Chat);
        });
        var taskB = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await quotaB.ReserveAsync(userId, LlmSurface.Chat);
        });

        var results = await Task.WhenAll(taskA, taskB);

        results.Count(r => r.Allowed).Should().Be(1, "exactly one reserver may cross the boundary");
        results.Count(r => !r.Allowed).Should().Be(1, "the other must be rejected");
        results.Single(r => !r.Allowed).DeniedReason.Should().Contain("hourly request limit");
    }

    [Fact]
    public async Task ReserveAsync_RepeatedConcurrentBursts_NeverOvershoot()
    {
        // Supplementary stress: repeated 4-way bursts, each on a fresh user at the single-slot boundary.
        // Every burst must admit exactly one — never two — proving the guarantee is not barrier-timing luck.
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var userId = Guid.NewGuid();
            const int racers = 4;
            using var barrier = new Barrier(racers);

            var tasks = Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var quota = scope.ServiceProvider.GetRequiredService<ILlmQuotaService>();
                barrier.SignalAndWait();
                return await quota.ReserveAsync(userId, LlmSurface.Chat);
            })).ToArray();

            var results = await Task.WhenAll(tasks);
            results.Count(r => r.Allowed).Should().Be(1, $"iteration {iteration}: only one slot exists");
        }
    }

    [Fact]
    public async Task ReleaseReservation_FreesSlot_NoQuotaLeak()
    {
        var userId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var quota = scope.ServiceProvider.GetRequiredService<ILlmQuotaService>();

        var first = await quota.ReserveAsync(userId, LlmSurface.Chat);
        first.Allowed.Should().BeTrue();
        first.ReservationId.Should().NotBeNull();

        // With the single slot held, a second reservation is rejected.
        var blocked = await quota.ReserveAsync(userId, LlmSurface.Chat);
        blocked.Allowed.Should().BeFalse();

        // Release simulates the LLM call failing / producing no usage — the slot must come back.
        await quota.ReleaseReservationAsync(first.ReservationId!.Value);

        var afterRelease = await quota.ReserveAsync(userId, LlmSurface.Chat);
        afterRelease.Allowed.Should().BeTrue("releasing a reservation must not permanently consume quota");
    }

    [Fact]
    public async Task CommitReservation_TurnsReservationIntoCountedUsage()
    {
        var userId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var quota = scope.ServiceProvider.GetRequiredService<ILlmQuotaService>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        var reservation = await quota.ReserveAsync(userId, LlmSurface.Chat);
        reservation.Allowed.Should().BeTrue();

        // A reservation is not yet real usage: reporting counts only committed rows.
        var pre = await repo.GetUsageSummaryAsync(userId, LlmSurface.Chat, from, to);
        pre.TotalRequests.Should().Be(0);

        await quota.CommitReservationAsync(reservation.ReservationId!.Value, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 123, 45);

        var post = await repo.GetUsageSummaryAsync(userId, LlmSurface.Chat, from, to);
        post.TotalRequests.Should().Be(1);
        post.TotalInputTokens.Should().Be(123);
        post.TotalOutputTokens.Should().Be(45);
    }

    [Fact]
    public async Task CommitReservation_AfterReservationSwept_RecoversBilledUsage()
    {
        // F1 (issue #1313): when an LLM call outlives ReservationTtlSeconds, the reservation row is
        // TTL-swept mid-call. The tokens were still genuinely billed, so committing must NOT silently
        // drop them — the repository inserts a replacement committed row so the usage still counts
        // against quota and telemetry, and a late/duplicate commit stays idempotent.
        var userId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var quota = scope.ServiceProvider.GetRequiredService<ILlmQuotaService>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        var reservation = await quota.ReserveAsync(userId, LlmSurface.Chat);
        reservation.Allowed.Should().BeTrue();
        var reservationId = reservation.ReservationId!.Value;

        // Simulate the TTL sweep deleting the reservation row while the slow LLM call was still running.
        using (var sweepScope = _factory.Services.CreateScope())
        {
            var db = sweepScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var row = await db.LlmUsageRecords.SingleAsync(r => r.Id == reservationId);
            db.LlmUsageRecords.Remove(row);
            await db.SaveChangesAsync();
        }

        // Commit must recover the billed tokens into a fresh committed row rather than no-op.
        await quota.CommitReservationAsync(reservationId, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 321, 12);

        var post = await repo.GetUsageSummaryAsync(userId, LlmSurface.Chat, from, to);
        post.TotalRequests.Should().Be(1, "the swept reservation's billed usage is recovered, not dropped");
        post.TotalInputTokens.Should().Be(321);
        post.TotalOutputTokens.Should().Be(12);

        // A duplicate/late commit for the same id must stay idempotent (no double-count).
        await quota.CommitReservationAsync(reservationId, userId, LlmSurface.Chat, "OpenAI", "gpt-4", 321, 12);
        var afterDup = await repo.GetUsageSummaryAsync(userId, LlmSurface.Chat, from, to);
        afterDup.TotalRequests.Should().Be(1, "a duplicate commit of an already-recovered id is a no-op");
        afterDup.TotalInputTokens.Should().Be(321);
    }

    [Fact]
    public async Task ReserveAsync_IgnoresAndSweeps_StaleReservation()
    {
        var userId = Guid.NewGuid();

        // Seed a stale reservation (a crashed process's orphan) that already expired and fills the slot.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var stale = LlmUsageRecord.CreateReservation(
                userId, LlmSurface.Chat, estimatedTokens: 500, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
            db.LlmUsageRecords.Add(stale);
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var quota = scope.ServiceProvider.GetRequiredService<ILlmQuotaService>();

        // The expired reservation must not count → the slot is available again.
        var reservation = await quota.ReserveAsync(userId, LlmSurface.Chat);
        reservation.Allowed.Should().BeTrue("an expired reservation must not consume quota");

        // And it must have been swept: only the one live reservation remains.
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var liveReserved = await checkDb.LlmUsageRecords
            .Where(r => r.UserId == userId && r.Status == LlmUsageRecordStatus.Reserved)
            .CountAsync();
        liveReserved.Should().Be(1, "the stale reservation is swept and one fresh reservation remains");
    }
}

/// <summary>
/// Factory pinning the daily token budget to exactly one reservation's estimate so the atomic
/// boundary (issue #1313) sits on the token/global SUM subqueries rather than the request count:
/// requests are unlimited, but the first reserver's estimate exhausts the token budget.
/// </summary>
public class SingleTokenSlotQuotaWebApplicationFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LlmQuotaSettings>();
            services.AddSingleton(new LlmQuotaSettings
            {
                RequestsPerHour = 0,               // unlimited: the token budget is the only gate
                TokensPerDay = 500,
                GlobalBudgetCeilingTokens = 0,
                ReservationEstimatedTokens = 500,  // one reservation's estimate fills the daily budget
                ReservationTtlSeconds = 120
            });
        });
    }
}

/// <summary>
/// Pins the token-budget half of the atomic reservation (issue #1313, finding F6): the concurrency
/// tests above race only the RequestsPerHour boundary, leaving the token/global SUM subqueries — the
/// budget-overshoot control — unproven. These race the daily-token boundary directly.
/// </summary>
public class LlmQuotaTokenBoundaryConcurrencyTests : IClassFixture<SingleTokenSlotQuotaWebApplicationFactory>
{
    private readonly SingleTokenSlotQuotaWebApplicationFactory _factory;

    public LlmQuotaTokenBoundaryConcurrencyTests(SingleTokenSlotQuotaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReserveAsync_TwoConcurrentAtTokenBoundary_ExactlyOnePasses()
    {
        var userId = Guid.NewGuid();

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var quotaA = scopeA.ServiceProvider.GetRequiredService<ILlmQuotaService>();
        var quotaB = scopeB.ServiceProvider.GetRequiredService<ILlmQuotaService>();

        // The reserved-token SUM subquery is evaluated under the writer lock, so the second reserver
        // sees the first's estimated 500 tokens and cannot also fit the 500-token budget.
        using var barrier = new Barrier(2);

        var taskA = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await quotaA.ReserveAsync(userId, LlmSurface.Chat);
        });
        var taskB = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await quotaB.ReserveAsync(userId, LlmSurface.Chat);
        });

        var results = await Task.WhenAll(taskA, taskB);

        results.Count(r => r.Allowed).Should().Be(1, "exactly one estimate fits the single-reservation token budget");
        results.Count(r => !r.Allowed).Should().Be(1, "the other must be rejected on the token budget");
        results.Single(r => !r.Allowed).DeniedReason.Should().Contain("daily token budget");
    }

    [Fact]
    public async Task ReserveAsync_RepeatedConcurrentBursts_NeverOvershootTokenBudget()
    {
        // Supplementary stress on the token boundary: repeated 4-way bursts on fresh users must each
        // admit exactly one, proving the token SUM serialization is not barrier-timing luck.
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var userId = Guid.NewGuid();
            const int racers = 4;
            using var barrier = new Barrier(racers);

            var tasks = Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var quota = scope.ServiceProvider.GetRequiredService<ILlmQuotaService>();
                barrier.SignalAndWait();
                return await quota.ReserveAsync(userId, LlmSurface.Chat);
            })).ToArray();

            var results = await Task.WhenAll(tasks);
            results.Count(r => r.Allowed).Should().Be(1, $"iteration {iteration}: the token budget admits exactly one reservation");
        }
    }
}
