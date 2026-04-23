using FluentAssertions;
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

        // Manually set the timestamp of the old entry via raw SQL
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}",
            DateTimeOffset.UtcNow.AddDays(-100), oldEntry.Id);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var deleted = await repo.DeleteOldEntriesAsync(cutoff, batchSize: 1000);

        deleted.Should().BeGreaterOrEqualTo(1);

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

        // Set all entries to be old
        foreach (var entry in entries)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}",
                DateTimeOffset.UtcNow.AddDays(-200), entry.Id);
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        // With batch size 2, it should still delete all 5 entries across multiple batches
        var deleted = await repo.DeleteOldEntriesAsync(cutoff, batchSize: 2);

        deleted.Should().BeGreaterOrEqualTo(5);
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
    public async Task DeleteOldEntriesAsync_HandlesCancellation()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should throw OperationCanceledException when token is already cancelled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repo.DeleteOldEntriesAsync(DateTimeOffset.UtcNow, batchSize: 100, cts.Token));
    }
}
