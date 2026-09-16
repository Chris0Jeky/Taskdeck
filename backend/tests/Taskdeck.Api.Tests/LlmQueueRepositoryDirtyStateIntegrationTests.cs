using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Focused real-SQLite regression for issue #1706. Kept separate from the broad repository
/// integration suite so the connected publication can preserve the regression-first commit.
/// </summary>
public sealed class LlmQueueRepositoryDirtyStateIntegrationTests
{
    [Fact]
    public async Task TryClaimProcessingCaptureAsync_ShouldPreserveUnrelatedDirtyTrackedFields()
    {
        await WithSqliteRepoAsync(async (db, repo) =>
        {
            var user = new User(
                "llm-claim-dirty-capture",
                "llm-claim-dirty-capture@example.com",
                "hash");
            db.Users.Add(user);

            const string originalPayload = "{\"text\":\"original-capture\"}";
            const string updatedPayload = "{\"text\":\"updated-capture\"}";
            var request = new LlmRequest(
                user.Id,
                CaptureRequestContract.RequestTypeV1,
                originalPayload);
            request.MarkAsProcessing();
            db.LlmRequests.Add(request);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var tracked = (await repo.GetOldestProcessingCaptureAsync(limit: 1))
                .Single(candidate => candidate.Id == request.Id);
            var expectedUpdatedAt = tracked.UpdatedAt;
            tracked.UpdatePayload(updatedPayload);

            var payloadProperty = db.Entry(tracked).Property(candidate => candidate.Payload);
            payloadProperty.OriginalValue.Should().Be(originalPayload);
            payloadProperty.CurrentValue.Should().Be(updatedPayload);
            payloadProperty.IsModified.Should().BeTrue();

            var claimed = await repo.TryClaimProcessingCaptureAsync(
                request.Id,
                expectedUpdatedAt);

            claimed.Should().BeTrue();
            payloadProperty.OriginalValue.Should().Be(originalPayload);
            payloadProperty.CurrentValue.Should().Be(updatedPayload);
            payloadProperty.IsModified.Should().BeTrue();

            var persistedClaim = await db.LlmRequests
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == request.Id);
            persistedClaim.Payload.Should().Be(originalPayload);
            tracked.Status.Should().Be(persistedClaim.Status);
            tracked.UpdatedAt.Should().Be(persistedClaim.UpdatedAt);

            await db.SaveChangesAsync();

            var persistedAfterSave = await db.LlmRequests
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == request.Id);
            persistedAfterSave.Payload.Should().Be(updatedPayload);
        });
    }

    private static async Task WithSqliteRepoAsync(
        Func<TaskdeckDbContext, LlmQueueRepository, Task> body)
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(),
            $"taskdeck-llmqueue-dirty-state-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;

            await using var db = new TaskdeckDbContext(options);
            await db.Database.MigrateAsync();
            await body(db, new LlmQueueRepository(db));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; assertion failures remain the useful signal.
                }
            }
        }
    }
}
