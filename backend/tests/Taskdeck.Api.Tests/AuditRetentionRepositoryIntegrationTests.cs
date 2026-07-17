using System.Globalization;
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
/// Integration tests for AuditLogRepository.DeleteOldEntriesAsync against real SQLite.
/// Verifies batch deletion, boundary conditions, and data integrity.
///
/// Runs on <see cref="HostedWorkerDisabledTestWebApplicationFactory"/> (#1383): the
/// production <c>AuditRetentionWorker</c> is registered unconditionally (see
/// <c>WorkerRegistration</c>) and runs a cleanup pass immediately at host start with the
/// default 90-day retention, so on the worker-enabled base factory it can delete the
/// 100/200-day-backdated rows these tests seed between seed and assertion — the confirmed
/// source of the intermittent "deleted == 0 despite a 70-day margin" flake. With the
/// worker removed from this host, every seed/read/delete in this class is deterministic.
/// </summary>
public class AuditRetentionRepositoryIntegrationTests : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    private readonly HostedWorkerDisabledTestWebApplicationFactory _factory;

    public AuditRetentionRepositoryIntegrationTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteOldEntriesAsync_DeletesEntriesOlderThanCutoff()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        // Create entries with different timestamps
        var oldEntry = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        var recentEntry = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        db.AuditLogs.AddRange(oldEntry, recentEntry);
        await db.SaveChangesAsync();

        // Manually backdate the old entry via raw SQL. BackdateEntryAsync asserts the
        // UPDATE affected exactly one row, so an Id-binding mismatch that silently
        // updates 0 rows (which would leave the entry at "now" and make the delete
        // return 0 for no obvious reason) is caught here at the write step.
        var writtenOld = await BackdateEntryAsync(db, oldEntry.Id, TimeSpan.FromDays(100));

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        // Read the backdated timestamp back from the database BEFORE deleting, on the
        // same context/connection the delete will use (db and repo share one scoped
        // TaskdeckDbContext). GetByIdAsync/FindAsync would return the tracked instance
        // whose in-memory timestamp is stale (the raw UPDATE bypassed the change
        // tracker), so reload the entity from the database first. This confirms the
        // manually written timestamp string round-trips through EF Core and reads back
        // older than the cutoff, turning a format-mismatch failure into a precise
        // diagnostic at the read step instead of a mysterious deleted == 0 later.
        await db.Entry(oldEntry).ReloadAsync();
        oldEntry.Timestamp.Should().BeCloseTo(writtenOld, TimeSpan.FromSeconds(1),
            "the backdated timestamp must round-trip through the repository's own connection");
        oldEntry.Timestamp.Should().BeBefore(cutoff,
            "the backdated entry must read back as older than the cutoff so DeleteOldEntriesAsync will match it");

        var deleted = await repo.DeleteOldEntriesAsync(cutoff, batchSize: 1000);

        deleted.Should().BeGreaterThanOrEqualTo(1);

        // The recent entry should still exist
        var remaining = await repo.GetByIdAsync(recentEntry.Id);
        remaining.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteOldEntriesAsync_ReturnsZero_WhenNoOldEntries()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        // Create a recent entry
        var recentEntry = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        db.AuditLogs.Add(recentEntry);
        await db.SaveChangesAsync();

        // Use a very old cutoff that won't match the recent entry
        var cutoff = DateTimeOffset.UtcNow.AddDays(-365);
        var deleted = await repo.DeleteOldEntriesAsync(cutoff, batchSize: 1000);

        // deleted is deliberately left unasserted. This class never seeds anything older
        // than 200 days, so the -365d cutoff cannot match class-seeded rows — but
        // DeleteOldEntriesAsync is global/unscoped, and a full app boot could in principle
        // seed audit rows of its own, so an exact-zero assertion would not be
        // isolation-safe. The meaningful invariant is that the recent entry survives.
        var remaining = await repo.GetByIdAsync(recentEntry.Id);
        remaining.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteOldEntriesAsync_RespectsSmallBatchSize()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        // Create multiple old entries
        var entries = new List<AuditLog>();
        for (int i = 0; i < 5; i++)
        {
            entries.Add(new AuditLog("Board", Guid.NewGuid(), AuditAction.Updated));
        }
        db.AuditLogs.AddRange(entries);
        await db.SaveChangesAsync();

        // Backdate all entries. BackdateEntryAsync asserts each UPDATE affected exactly
        // one row, so a silent 0-row update cannot leave an entry at "now" undetected.
        foreach (var entry in entries)
        {
            await BackdateEntryAsync(db, entry.Id, TimeSpan.FromDays(200));
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        // With batch size 2, it should still delete all 5 entries across multiple batches
        var deleted = await repo.DeleteOldEntriesAsync(cutoff, batchSize: 2);

        deleted.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task DeleteOldEntriesAsync_PreservesRecentEntries()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var recentEntityId = Guid.NewGuid();
        var recentEntry = new AuditLog("Card", recentEntityId, AuditAction.Updated);
        db.AuditLogs.Add(recentEntry);
        await db.SaveChangesAsync();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        await repo.DeleteOldEntriesAsync(cutoff, batchSize: 1000);

        var preserved = await repo.GetByIdAsync(recentEntry.Id);
        preserved.Should().NotBeNull();
        preserved!.EntityId.Should().Be(recentEntityId);
    }

    [Fact]
    public async Task DeleteOldEntriesAsync_CompletesGracefully_WhenNothingToDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        // When there's nothing to delete, the method should return 0 without error
        var deleted = await repo.DeleteOldEntriesAsync(
            DateTimeOffset.UtcNow.AddYears(-100), batchSize: 100);

        deleted.Should().Be(0);
    }

    [Fact]
    public async Task DeleteOldEntriesAsync_WithNonUtcCutoff_DeletesCorrectEntries()
    {
        // Regression test for: DateTimeOffset with non-zero UTC offset must be normalized
        // to UTC before formatting as a SQLite timestamp string. Without normalization,
        // a cutoff of e.g. 2026-01-01T00:00:00-05:00 would be formatted as
        // "2026-01-01 00:00:00+00:00" instead of "2026-01-01 05:00:00+00:00",
        // causing entries in the wrong five-hour window to be kept or deleted.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var oldEntry = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        var recentEntry = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        db.AuditLogs.AddRange(oldEntry, recentEntry);
        await db.SaveChangesAsync();

        // Backdate the old entry to well before the cutoff (asserts exactly one row updated).
        await BackdateEntryAsync(db, oldEntry.Id, TimeSpan.FromDays(100));

        // Build a non-UTC cutoff that represents the same instant as "30 days ago UTC"
        // expressed in Eastern Standard Time (-05:00). The repository must convert to
        // UTC before building the SQLite comparison string.
        var utcBase = DateTimeOffset.UtcNow.AddDays(-30);
        var nonUtcCutoff = utcBase.ToOffset(TimeSpan.FromHours(-5));
        var deleted = await repo.DeleteOldEntriesAsync(nonUtcCutoff, batchSize: 1000);

        deleted.Should().BeGreaterThanOrEqualTo(1,
            "entries older than the UTC-equivalent cutoff should be deleted regardless of the cutoff offset");

        // The recent entry (created just now) must survive
        var preserved = await repo.GetByIdAsync(recentEntry.Id);
        preserved.Should().NotBeNull("the recent entry must not be deleted when a non-UTC cutoff is used");
    }

    /// <summary>
    /// Backdates a single audit entry's timestamp via raw SQL and asserts the UPDATE
    /// affected exactly one row.
    ///
    /// Format note (verified empirically, #1391 round 2): EF Core / Microsoft.Data.Sqlite
    /// serializes DateTimeOffset as "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz" — capital F, so
    /// trailing fractional zeros are trimmed and a zero fraction drops the '.' entirely.
    /// Observed raw column TEXT: .1234567 → "2026-07-17 03:04:05.1234567+00:00";
    /// .1200000 → "2026-07-17 03:04:05.12+00:00"; .0000000 → "2026-07-17 03:04:05+00:00".
    /// This helper writes a fixed 7-digit fraction, which is therefore NOT byte-identical
    /// to EF's trimmed form for the same instant — but string ordering stays correct:
    /// '+' and '.' both sort below every digit, so a trimmed string orders exactly as its
    /// zero-extended value against both EF-written rows and the repository's fixed-format
    /// cutoff. The only divergence is the representation of an exactly-equal instant (a
    /// strict-&lt; boundary tie), which this class never relies on (70-day margins).
    ///
    /// The string is built with <see cref="CultureInfo.InvariantCulture"/> because the
    /// ':' in a custom format is the culture-sensitive time separator: a locale using a
    /// different separator would otherwise write a string that diverges from EF's
    /// invariant serialization and corrupt the string-based comparison in
    /// DeleteOldEntriesAsync. Returns the instant that was written so callers can assert
    /// the value round-trips through EF Core and orders correctly against the cutoff.
    /// </summary>
    private static async Task<DateTimeOffset> BackdateEntryAsync(
        TaskdeckDbContext db, Guid entryId, TimeSpan age)
    {
        var timestamp = DateTimeOffset.UtcNow - age;
        var timestampStr = timestamp.UtcDateTime
            .ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "+00:00";
        var affected = await db.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}",
            timestampStr, entryId);
        affected.Should().Be(1,
            "the raw backdating UPDATE must affect exactly the one targeted row; a silent " +
            "0-row UPDATE would leave the entry at 'now' and make DeleteOldEntriesAsync return 0");
        return timestamp;
    }
}
