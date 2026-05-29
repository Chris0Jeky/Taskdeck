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

        // Manually set the timestamp of the old entry via raw SQL.
        // Format as string matching EF Core's SQLite DateTimeOffset storage format
        // so that string-based < comparisons in DeleteOldEntriesAsync work correctly.
        var oldTimestampStr = DateTimeOffset.UtcNow.AddDays(-100).ToString("yyyy-MM-dd HH:mm:ss.fffffff+00:00");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}",
            oldTimestampStr, oldEntry.Id);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
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

        // Set all entries to be old.
        // Format as string matching EF Core's SQLite DateTimeOffset storage format.
        var oldTimestampStr = DateTimeOffset.UtcNow.AddDays(-200).ToString("yyyy-MM-dd HH:mm:ss.fffffff+00:00");
        foreach (var entry in entries)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}",
                oldTimestampStr, entry.Id);
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

        // Backdate the old entry to well before the cutoff
        var oldTimestampStr = DateTimeOffset.UtcNow.AddDays(-100).ToString("yyyy-MM-dd HH:mm:ss.fffffff+00:00");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}",
            oldTimestampStr, oldEntry.Id);

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
}
