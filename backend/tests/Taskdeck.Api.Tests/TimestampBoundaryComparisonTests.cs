using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Boundary regression tests for issue #1403: production repositories previously built SQLite
/// DateTimeOffset comparison bounds as fixed-width ".fffffff" strings, but EF Core's SQLite
/// provider stores DateTimeOffset TEXT with trailing fraction zeros TRIMMED and the dot dropped
/// entirely at a zero fraction (empirically verified on PR #1391): a whole-second instant stores
/// as "2026-07-17 00:00:00+00:00", not "...00:00:00.0000000+00:00".
///
/// At an exact zero-fraction boundary tick the stored TEXT sorts BELOW the hand-built bound string
/// because after the shared "...HH:mm:ss" prefix the stored value continues with '+' (0x2B) while
/// the fixed-width bound continues with '.' (0x2E), and '+' &lt; '.'. That off-by-one-tick violated
/// four contracts. The fix parameterizes each comparison with the DateTimeOffset value itself, so EF
/// serializes both the bound and the stored column with the same trimmed mapping and a zero-fraction
/// boundary row compares equal.
///
/// Each test seeds the stored value at EXACTLY the bound with ZERO fractional ticks, written in the
/// real EF-trimmed shape (no '.' fraction, e.g. "2023-03-04 00:00:00+00:00"), so the boundary is
/// genuinely exercised. Under the pre-fix code these assertions fail; a comment at each site cites
/// the '+' vs '.' byte comparison.
///
/// Runs on <see cref="HostedWorkerDisabledTestWebApplicationFactory"/> (mirrors
/// <c>AuditRetentionRepositoryIntegrationTests</c> / <c>AuditCultureInvariantRepositoryTests</c>) so
/// the unconditionally-registered <c>AuditRetentionWorker</c> cannot delete seeded audit rows between
/// seed and assertion. Each test uses a distinct year so the class-shared database cannot leak rows
/// between methods.
/// </summary>
public class TimestampBoundaryComparisonTests : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    private readonly HostedWorkerDisabledTestWebApplicationFactory _factory;

    public TimestampBoundaryComparisonTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CountByDateAsync_CountsRowStoredAtExactlyFrom_WithZeroFractionTick()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        // AuditLog.UserId is a real FK to Users, so seed a user to own the row.
        var user = new User("ts-boundary-count", "ts-boundary-count@example.com", "hash");
        db.Users.Add(user);

        var entry = new AuditLog("Card", Guid.NewGuid(), AuditAction.Updated, user.Id);
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync();

        // Stored at EXACTLY the `from` bound, zero fractional ticks, in the EF-trimmed shape (no '.').
        await SetTimestampAsync(db, entry.Id, "2023-03-04 00:00:00+00:00");

        var from = new DateTimeOffset(2023, 3, 4, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);

        var counts = await repo.CountByDateAsync(from, to, user.Id);

        // Contract: `Timestamp >= from` is inclusive. Pre-fix, `from` was built as
        // "2023-03-04 00:00:00.0000000+00:00" and the stored "2023-03-04 00:00:00+00:00" sorted below
        // it ('+' 0x2B < '.' 0x2E), so the boundary row was WRONGLY excluded and the range returned 0.
        counts.Should().ContainSingle(
            "a row stored at exactly `from` with a zero-fraction tick must be counted (>= is inclusive)");
        counts[0].Date.Should().Be(new DateOnly(2023, 3, 4));
        counts[0].Count.Should().Be(1);
    }

    [Fact]
    public async Task DeleteOldEntriesAsync_KeepsRowStoredAtExactlyCutoff_WithZeroFractionTick()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var atCutoff = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        var strictlyOlder = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        db.AuditLogs.AddRange(atCutoff, strictlyOlder);
        await db.SaveChangesAsync();

        // atCutoff sits at EXACTLY the cutoff with a zero-fraction tick (EF-trimmed shape); strictlyOlder
        // is one day earlier so at least one row is genuinely deletable.
        await SetTimestampAsync(db, atCutoff.Id, "2019-03-04 00:00:00+00:00");
        await SetTimestampAsync(db, strictlyOlder.Id, "2019-03-03 00:00:00+00:00");

        var cutoff = new DateTimeOffset(2019, 3, 4, 0, 0, 0, TimeSpan.Zero);

        var deleted = await repo.DeleteOldEntriesAsync(cutoff, batchSize: 1000);
        deleted.Should().BeGreaterThanOrEqualTo(1, "the strictly-older row must be deleted");

        // Verify against the database (AsNoTracking): the raw-SQL DELETE bypasses the change tracker.
        var survivors = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Id == atCutoff.Id || a.Id == strictlyOlder.Id)
            .Select(a => a.Id)
            .ToListAsync();

        // Contract: `Timestamp < cutoff` is STRICTLY older. Pre-fix, cutoff was built as
        // "2019-03-04 00:00:00.0000000+00:00" and the stored "2019-03-04 00:00:00+00:00" sorted below it
        // ('+' 0x2B < '.' 0x2E), so `< cutoff` was WRONGLY true and the boundary row was deleted.
        survivors.Should().Contain(atCutoff.Id,
            "a row stored at exactly the cutoff with a zero-fraction tick must NOT be deleted (< is strict)");
        survivors.Should().NotContain(strictlyOlder.Id, "the strictly-older row must be deleted");
    }

    [Fact]
    public async Task DeleteExpiredAsync_KeepsCodeExpiringAtExactlyCutoff_WithZeroFractionTick()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IOAuthAuthCodeRepository>();

        // Construct with a valid future expiry (entity invariant), then backdate ExpiresAt via raw SQL.
        var atCutoff = new OAuthAuthCode(
            $"bound-{Guid.NewGuid():N}", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddMinutes(5));
        var strictlyExpired = new OAuthAuthCode(
            $"expired-{Guid.NewGuid():N}", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddMinutes(5));
        db.Set<OAuthAuthCode>().AddRange(atCutoff, strictlyExpired);
        await db.SaveChangesAsync();

        // atCutoff expires at EXACTLY the cutoff with a zero-fraction tick (EF-trimmed shape).
        await SetExpiresAtAsync(db, atCutoff.Id, "2024-03-04 00:00:00+00:00");
        await SetExpiresAtAsync(db, strictlyExpired.Id, "2024-03-03 00:00:00+00:00");

        var cutoff = new DateTimeOffset(2024, 3, 4, 0, 0, 0, TimeSpan.Zero);

        var deleted = await repo.DeleteExpiredAsync(cutoff);
        deleted.Should().BeGreaterThanOrEqualTo(1, "the strictly-expired code must be deleted");

        var remaining = await db.Set<OAuthAuthCode>().AsNoTracking()
            .Where(c => c.Id == atCutoff.Id || c.Id == strictlyExpired.Id)
            .Select(c => c.Id)
            .ToListAsync();

        // Contract: `ExpiresAt < cutoff` is STRICTLY older. Pre-fix, the stored zero-fraction
        // "2024-03-04 00:00:00+00:00" sorted below the fixed-width cutoff ('+' 0x2B < '.' 0x2E), so
        // `< cutoff` was WRONGLY true and the code was deleted at its exact expiry boundary.
        remaining.Should().Contain(atCutoff.Id,
            "a code expiring at exactly the cutoff with a zero-fraction tick must NOT be deleted (< is strict)");
        remaining.Should().NotContain(strictlyExpired.Id, "the strictly-expired code must be deleted");
    }

    [Fact]
    public async Task TryConsumeAtomicAsync_PinsStrictExpiryContract_WithZeroFractionTicks()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IOAuthAuthCodeRepository>();

        // Live control: a whole-second FUTURE expiry, seeded THROUGH EF so ExpiresAt is stored in the
        // real trimmed zero-fraction shape ("2099-01-01 00:00:00+00:00"). `ExpiresAt > now` is true.
        var liveCodeValue = $"ts-live-{Guid.NewGuid():N}";
        var liveCode = new OAuthAuthCode(
            liveCodeValue, Guid.NewGuid(), "token", new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // Expired: construct with a valid future expiry (entity invariant), then backdate to a past
        // whole-second value with a zero fractional tick via raw SQL.
        var expiredCodeValue = $"ts-expired-{Guid.NewGuid():N}";
        var expiredCode = new OAuthAuthCode(
            expiredCodeValue, Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddMinutes(5));

        db.Set<OAuthAuthCode>().AddRange(liveCode, expiredCode);
        await db.SaveChangesAsync();

        await SetExpiresAtAsync(db, expiredCode.Id, "2020-01-01 00:00:00+00:00");

        // Contract (see TryConsumeAtomicAsync): consumption requires `ExpiresAt > now` — STRICTLY
        // greater — so a code at or before the current instant is expired and MUST NOT consume. `now`
        // is DateTimeOffset.UtcNow inside the method and cannot be injected without changing the
        // signature (out of scope), so this pins the strict contract with genuinely zero-fraction
        // stored values on both sides of the boundary rather than forcing exact-instant equality.
        //
        // Unlike the `>=` / `<` sites, the `>` operator returns the same answer at exact equality even
        // under the pre-fix string bug (stored-below-bound and true-equality both yield "not
        // consumable"); parameterizing keeps `ExpiresAt > now` chronological for consistency and pins
        // the strict-greater contract against regression.
        var consumedExpired = await repo.TryConsumeAtomicAsync(expiredCodeValue);
        consumedExpired.Should().BeFalse(
            "a code whose zero-fraction ExpiresAt is at or before now must NOT consume (> is strict)");

        var consumedLive = await repo.TryConsumeAtomicAsync(liveCodeValue);
        consumedLive.Should().BeTrue(
            "an unexpired code with a zero-fraction future ExpiresAt must consume successfully");

        var reloaded = await db.Set<OAuthAuthCode>().AsNoTracking()
            .SingleAsync(c => c.Id == liveCode.Id);
        reloaded.IsConsumed.Should().BeTrue("the live code must be marked consumed by the atomic update");
    }

    private static Task SetTimestampAsync(TaskdeckDbContext db, Guid id, string invariantTimestamp)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}", invariantTimestamp, id);

    private static Task SetExpiresAtAsync(TaskdeckDbContext db, Guid id, string invariantTimestamp)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE OAuthAuthCodes SET ExpiresAt = {0} WHERE Id = {1}", invariantTimestamp, id);
}
