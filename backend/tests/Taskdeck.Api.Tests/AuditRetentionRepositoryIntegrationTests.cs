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
/// </summary>
public class AuditRetentionRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuditRetentionRepositoryIntegrationTests(TestWebApplicationFactory factory)
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

        // Read the backdated timestamp back through the repository's own context/
        // connection BEFORE deleting. GetByIdAsync/FindAsync would return the tracked
        // instance whose in-memory timestamp is stale (the raw UPDATE bypassed the
        // change tracker), so reload the entity from the database first. This turns a
        // cross-connection visibility or timestamp-format failure into a precise
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

        // May delete entries from other tests, but the important thing is
        // our recent entry should still exist
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
    /// affected exactly one row. The timestamp string is built with
    /// <see cref="CultureInfo.InvariantCulture"/> so it matches EF Core's invariant
    /// SQLite DateTimeOffset serialization ("yyyy-MM-dd HH:mm:ss.fffffff+00:00")
    /// regardless of the host's current culture: a locale whose time separator is not
    /// ':' would otherwise write a mismatched string and corrupt the string-based
    /// comparison in DeleteOldEntriesAsync. Returns the instant that was written so
    /// callers can assert visibility/ordering through the repository's own connection.
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
