using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for LlmQueueRepository against real SQLite.
/// Covers concurrent claims, json_extract correctness, GUID format, and query ordering.
/// </summary>
public class LlmQueueRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LlmQueueRepositoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetPendingAsync_ShouldReturnPendingOrderedByCreatedAtAsc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-pending-user", "llm-pending@example.com", "hash");
        db.Users.Add(user);

        // Create requests with different statuses — only Pending should appear
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pendingFirst = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"first\"}");
        var pendingSecond = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"second\"}");
        var processing = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"processing\"}");
        processing.MarkAsProcessing();

        db.LlmRequests.AddRange(pendingFirst, pendingSecond, processing);
        await db.SaveChangesAsync();

        // Set explicit timestamps for deterministic ordering
        db.Entry(pendingFirst).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime;
        db.Entry(pendingSecond).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime.AddSeconds(1);
        await db.SaveChangesAsync();

        // Clear the change tracker so the repository query reads fresh data from the database
        // and returns results in the SQL-specified ORDER BY order.
        db.ChangeTracker.Clear();

        // Use a large limit because the shared database may contain pending requests
        // from other test classes seeded via IClassFixture.
        var result = (await repo.GetPendingAsync(limit: 500)).ToList();

        result.Should().Contain(r => r.Id == pendingFirst.Id);
        result.Should().Contain(r => r.Id == pendingSecond.Id);
        result.Should().NotContain(r => r.Id == processing.Id);

        // Verify ordering: first created should appear first (ASC)
        var firstIdx = result.FindIndex(r => r.Id == pendingFirst.Id);
        var secondIdx = result.FindIndex(r => r.Id == pendingSecond.Id);
        firstIdx.Should().BeLessThan(secondIdx);
    }

    [Fact]
    public async Task GetPendingAsync_WithLimit_ShouldRespectLimit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-limit-user", "llm-limit@example.com", "hash");
        db.Users.Add(user);

        for (var i = 0; i < 5; i++)
        {
            db.LlmRequests.Add(new LlmRequest(user.Id, "inbox.capture.text", $"{{\"i\":{i}}}"));
        }
        await db.SaveChangesAsync();

        var result = (await repo.GetPendingAsync(limit: 2)).ToList();

        result.Count.Should().BeLessThanOrEqualTo(2);
    }

    // The bounded work-drain reads target Pending-non-capture and Processing-capture rows -- exactly the
    // rows the live worker claims. These tests therefore use an isolated, worker-free SQLite context
    // (no web host) so the background worker cannot mutate the rows mid-assertion.

    [Fact]
    public async Task GetOldestPendingNonCaptureAsync_ReturnsOldestNonCaptureBounded_ExcludesCapturesAndBoundsAtSql()
    {
        var interceptor = new CapturingReaderInterceptor();
        await WithSqliteRepoAsync(async (db, repo) =>
        {
            var user = new User("llm-pending-noncap", "llm-pending-noncap@example.com", "hash");
            db.Users.Add(user);

            // Capture rows are OLDER than the automation rows. A naive bound on raw Pending status
            // (oldest-first) would return only these, and a post-fetch non-capture filter would then
            // yield nothing -- starving automation work. The in-query predicate must exclude them.
            var captures = new List<LlmRequest>();
            for (var i = 0; i < 3; i++)
            {
                var c = new LlmRequest(user.Id, CaptureRequestContract.RequestTypeV1, $"{{\"c\":{i}}}");
                db.LlmRequests.Add(c);
                captures.Add(c);
            }
            var automation = new List<LlmRequest>();
            for (var i = 0; i < 5; i++)
            {
                var a = new LlmRequest(user.Id, "automation.command", $"{{\"a\":{i}}}");
                db.LlmRequests.Add(a);
                automation.Add(a);
            }
            // A non-capture row in another status must not leak into the Pending read.
            var completed = new LlmRequest(user.Id, "automation.command", "{\"done\":true}");
            completed.MarkAsProcessing();
            completed.MarkAsCompleted();
            db.LlmRequests.Add(completed);
            await db.SaveChangesAsync();

            var olderCapture = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var newerAutomation = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < captures.Count; i++)
                db.Entry(captures[i]).Property(nameof(Entity.CreatedAt)).CurrentValue = olderCapture.AddSeconds(i);
            for (var i = 0; i < automation.Count; i++)
                db.Entry(automation[i]).Property(nameof(Entity.CreatedAt)).CurrentValue = newerAutomation.AddSeconds(i);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            interceptor.Clear();

            var result = (await repo.GetOldestPendingNonCaptureAsync(3)).ToList();

            result.Should().HaveCount(3);
            result.Should().OnlyContain(r => !CaptureRequestContract.IsCaptureRequestType(r.RequestType));
            // Oldest-first among NON-capture Pending rows; the older captures are excluded in-query
            // (anti-starvation) and the Completed row is excluded by status.
            result.Select(r => r.Id).Should().Equal(automation[0].Id, automation[1].Id, automation[2].Id);

            // The bound is enforced at the database (LIMIT), not by materializing then slicing in memory.
            interceptor.CapturedCommands
                .Where(sql => sql.Contains("LlmRequests", StringComparison.OrdinalIgnoreCase)
                              && sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                .Should().Contain(sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase),
                    "the bounded read must push LIMIT into SQL");
        }, interceptor);
    }

    [Fact]
    public async Task GetOldestProcessingCaptureAsync_ReturnsOldestCaptureBounded_ExcludesNonCapture()
    {
        await WithSqliteRepoAsync(async (db, repo) =>
        {
            var user = new User("llm-proc-cap", "llm-proc-cap@example.com", "hash");
            db.Users.Add(user);

            // Non-capture Processing rows are OLDER; the in-query capture predicate must exclude them so
            // capture-triage is not starved behind claimed/orphaned non-capture Processing rows.
            var nonCapture = new List<LlmRequest>();
            for (var i = 0; i < 3; i++)
            {
                var r = new LlmRequest(user.Id, "automation.command", $"{{\"n\":{i}}}");
                r.MarkAsProcessing();
                db.LlmRequests.Add(r);
                nonCapture.Add(r);
            }
            var captures = new List<LlmRequest>();
            for (var i = 0; i < 5; i++)
            {
                var c = new LlmRequest(user.Id, CaptureRequestContract.RequestTypeV1, $"{{\"c\":{i}}}");
                c.MarkAsProcessing();
                db.LlmRequests.Add(c);
                captures.Add(c);
            }
            await db.SaveChangesAsync();

            var older = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var newer = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < nonCapture.Count; i++)
                db.Entry(nonCapture[i]).Property(nameof(Entity.CreatedAt)).CurrentValue = older.AddSeconds(i);
            for (var i = 0; i < captures.Count; i++)
                db.Entry(captures[i]).Property(nameof(Entity.CreatedAt)).CurrentValue = newer.AddSeconds(i);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = (await repo.GetOldestProcessingCaptureAsync(3)).ToList();

            result.Should().HaveCount(3);
            result.Should().OnlyContain(r => CaptureRequestContract.IsCaptureRequestType(r.RequestType));
            result.Select(r => r.Id).Should().Equal(captures[0].Id, captures[1].Id, captures[2].Id);
        });
    }

    [Fact]
    public async Task GetOldestWorkReads_WithLimitBelowOne_Throw()
    {
        await WithSqliteRepoAsync(async (_, repo) =>
        {
            await repo.Invoking(r => r.GetOldestPendingNonCaptureAsync(0))
                .Should().ThrowAsync<ArgumentOutOfRangeException>();
            await repo.Invoking(r => r.GetOldestProcessingCaptureAsync(-1))
                .Should().ThrowAsync<ArgumentOutOfRangeException>();
        });
    }

    [Fact]
    public async Task BacklogCounts_CountOnlyMatchingKind_Unbounded()
    {
        await WithSqliteRepoAsync(async (db, repo) =>
        {
            var user = new User("llm-counts", "llm-counts@example.com", "hash");
            db.Users.Add(user);

            for (var i = 0; i < 4; i++)
                db.LlmRequests.Add(new LlmRequest(user.Id, "automation.command", "{}")); // Pending non-capture
            for (var i = 0; i < 2; i++)
                db.LlmRequests.Add(new LlmRequest(user.Id, CaptureRequestContract.RequestTypeV1, "{}")); // Pending capture
            for (var i = 0; i < 3; i++)
            {
                var c = new LlmRequest(user.Id, CaptureRequestContract.RequestTypeV1, "{}"); // Processing capture
                c.MarkAsProcessing();
                db.LlmRequests.Add(c);
            }
            var n = new LlmRequest(user.Id, "automation.command", "{}"); // Processing non-capture
            n.MarkAsProcessing();
            db.LlmRequests.Add(n);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await repo.CountPendingNonCaptureAsync()).Should().Be(4);
            (await repo.CountProcessingCaptureAsync()).Should().Be(3);
        });
    }

    [Fact]
    public async Task GetCapturesByUserAsync_ReturnsCaptureOnlyPage_NewestFirst_PerUser_BoundedAtSql()
    {
        var interceptor = new CapturingReaderInterceptor();
        await WithSqliteRepoAsync(async (db, repo) =>
        {
            var userA = new User("cap-page-a", "cap-page-a@example.com", "hash");
            var userB = new User("cap-page-b", "cap-page-b@example.com", "hash");
            db.Users.AddRange(userA, userB);

            var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var captures = new List<LlmRequest>();
            for (var i = 0; i < 3; i++)
            {
                var c = new LlmRequest(userA.Id, CaptureRequestContract.RequestTypeV1, $"{{\"i\":{i}}}");
                db.LlmRequests.Add(c);
                captures.Add(c);
            }
            // Same user, non-capture rows must be excluded; other user's capture must be excluded.
            db.LlmRequests.Add(new LlmRequest(userA.Id, "automation.command", "{}"));
            db.LlmRequests.Add(new LlmRequest(userA.Id, "automation.command", "{}"));
            db.LlmRequests.Add(new LlmRequest(userB.Id, CaptureRequestContract.RequestTypeV1, "{}"));
            await db.SaveChangesAsync();

            for (var i = 0; i < captures.Count; i++)
                db.Entry(captures[i]).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime.AddSeconds(i);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            interceptor.Clear();

            var result = (await repo.GetCapturesByUserAsync(userA.Id, 10, 0)).ToList();

            result.Should().HaveCount(3);
            result.Should().OnlyContain(r => r.UserId == userA.Id && CaptureRequestContract.IsCaptureRequestType(r.RequestType));
            // Newest-first: captures[2] (latest CreatedAt) first.
            result.Select(r => r.Id).Should().Equal(captures[2].Id, captures[1].Id, captures[0].Id);

            interceptor.CapturedCommands
                .Where(sql => sql.Contains("LlmRequests", StringComparison.OrdinalIgnoreCase)
                              && sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                .Should().Contain(
                    sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                           && sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase),
                    "the paged read must push LIMIT and OFFSET into SQL");
        }, interceptor);
    }

    [Fact]
    public async Task GetCapturesByUserAsync_PagesStably_NoOverlapOrSkip()
    {
        await WithSqliteRepoAsync(async (db, repo) =>
        {
            var user = new User("cap-paging", "cap-paging@example.com", "hash");
            db.Users.Add(user);

            var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var captures = new List<LlmRequest>();
            for (var i = 0; i < 5; i++)
            {
                var c = new LlmRequest(user.Id, CaptureRequestContract.RequestTypeV1, $"{{\"i\":{i}}}");
                db.LlmRequests.Add(c);
                captures.Add(c);
            }
            await db.SaveChangesAsync();
            for (var i = 0; i < captures.Count; i++)
                db.Entry(captures[i]).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime.AddSeconds(i);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var page1 = (await repo.GetCapturesByUserAsync(user.Id, 2, 0)).Select(r => r.Id).ToList();
            var page2 = (await repo.GetCapturesByUserAsync(user.Id, 2, 2)).Select(r => r.Id).ToList();
            var page3 = (await repo.GetCapturesByUserAsync(user.Id, 2, 4)).Select(r => r.Id).ToList();

            // Newest-first across pages, no row skipped or duplicated.
            page1.Should().Equal(captures[4].Id, captures[3].Id);
            page2.Should().Equal(captures[2].Id, captures[1].Id);
            page3.Should().Equal(captures[0].Id);
            page1.Concat(page2).Concat(page3).Should().OnlyHaveUniqueItems().And.HaveCount(5);
        });
    }

    [Fact]
    public async Task GetCapturesByUserAsync_WithInvalidPaging_Throws()
    {
        await WithSqliteRepoAsync(async (_, repo) =>
        {
            var userId = Guid.NewGuid();
            await repo.Invoking(r => r.GetCapturesByUserAsync(userId, 0, 0))
                .Should().ThrowAsync<ArgumentOutOfRangeException>();
            await repo.Invoking(r => r.GetCapturesByUserAsync(userId, 5, -1))
                .Should().ThrowAsync<ArgumentOutOfRangeException>();
        });
    }

    [Fact]
    public async Task TryClaimProcessingCaptureAsync_ShouldSucceedOnFirstClaim_FailOnSecond()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-claim-user", "llm-claim@example.com", "hash");
        db.Users.Add(user);

        var request = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"claim-test\"}");
        request.MarkAsProcessing();
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        var expectedUpdatedAt = request.UpdatedAt;

        // Use separate scopes + Task.WhenAll for truly concurrent claims
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var firstRepo = firstScope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();
        var secondRepo = secondScope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var results = await Task.WhenAll(
            firstRepo.TryClaimProcessingCaptureAsync(request.Id, expectedUpdatedAt),
            secondRepo.TryClaimProcessingCaptureAsync(request.Id, expectedUpdatedAt));

        // Exactly one should succeed (optimistic concurrency)
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task TryClaimProcessingCaptureAsync_ShouldRejectNonCaptureRequestType()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-noncapture-user", "llm-noncapture@example.com", "hash");
        db.Users.Add(user);

        // RequestType does NOT match "inbox.capture.%" — claim should fail
        var request = new LlmRequest(user.Id, "chat.completion", "{\"text\":\"not-a-capture\"}");
        request.MarkAsProcessing();
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        var result = await repo.TryClaimProcessingCaptureAsync(
            request.Id, request.UpdatedAt);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetCaptureSummaryByUserAsync_ShouldCountByStatusForCaptureRequests()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-summary-user", "llm-summary@example.com", "hash");
        db.Users.Add(user);

        // Create capture requests in various statuses
        var pending = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"p\"}");
        var processing = new LlmRequest(user.Id, "inbox.capture.voice", "{\"text\":\"proc\"}");
        processing.MarkAsProcessing();
        var failed = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"fail\"}");
        failed.MarkAsProcessing();
        failed.MarkAsFailed("error");
        var completed = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"done\"}");
        completed.MarkAsProcessing();
        completed.MarkAsCompleted();

        // Non-capture request should NOT be counted
        var nonCapture = new LlmRequest(user.Id, "chat.completion", "{\"text\":\"nc\"}");

        db.LlmRequests.AddRange(pending, processing, failed, completed, nonCapture);
        await db.SaveChangesAsync();

        var summary = await repo.GetCaptureSummaryByUserAsync(user.Id);

        summary.TotalCaptures.Should().Be(4); // pending + processing + failed + completed
        summary.NewCount.Should().Be(1);       // pending
        summary.FailedCount.Should().Be(1);    // failed
        summary.TriagingCount.Should().Be(1);  // processing
    }

    [Fact]
    public async Task GetCaptureSummaryByUserAsync_WithProposalPayload_ShouldReduceTriagedCount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-triage-user", "llm-triage@example.com", "hash");
        db.Users.Add(user);

        // Completed capture WITH proposalId in payload — should reduce triaged count
        var withProposal = new LlmRequest(
            user.Id,
            "inbox.capture.text",
            "{\"text\":\"linked\",\"provenance\":{\"proposalId\":\"abc-123\"}}");
        withProposal.MarkAsProcessing();
        withProposal.MarkAsCompleted();

        // Completed capture WITHOUT proposalId — counts as triaged
        var withoutProposal = new LlmRequest(
            user.Id,
            "inbox.capture.text",
            "{\"text\":\"orphan\"}");
        withoutProposal.MarkAsProcessing();
        withoutProposal.MarkAsCompleted();

        db.LlmRequests.AddRange(withProposal, withoutProposal);
        await db.SaveChangesAsync();

        var summary = await repo.GetCaptureSummaryByUserAsync(user.Id);

        // 2 completed total, 1 has proposalId → triaged = 2 - 1 = 1
        summary.TriagedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetByUserAsync_ShouldReturnOnlyUserRequests_OrderedByCreatedAtDesc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var userA = new User("llm-usera", "llm-usera@example.com", "hash");
        var userB = new User("llm-userb", "llm-userb@example.com", "hash");
        db.Users.AddRange(userA, userB);

        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var reqAOlder = new LlmRequest(userA.Id, "inbox.capture.text", "{\"text\":\"a-older\"}");
        var reqANewer = new LlmRequest(userA.Id, "inbox.capture.text", "{\"text\":\"a-newer\"}");
        var reqB = new LlmRequest(userB.Id, "inbox.capture.text", "{\"text\":\"b\"}");
        db.LlmRequests.AddRange(reqAOlder, reqANewer, reqB);
        await db.SaveChangesAsync();

        // Set explicit timestamps for deterministic ordering
        db.Entry(reqAOlder).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime;
        db.Entry(reqANewer).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime.AddSeconds(1);
        await db.SaveChangesAsync();

        // Clear the change tracker so the repository query reads fresh data from the database
        // and returns results in the SQL-specified ORDER BY CreatedAt DESC order,
        // rather than serving tracked entities from the identity map in insertion order.
        db.ChangeTracker.Clear();

        var resultA = (await repo.GetByUserAsync(userA.Id)).ToList();
        var resultB = (await repo.GetByUserAsync(userB.Id)).ToList();

        // User isolation
        resultA.Should().Contain(r => r.Id == reqAOlder.Id);
        resultA.Should().Contain(r => r.Id == reqANewer.Id);
        resultA.Should().NotContain(r => r.Id == reqB.Id);
        resultB.Should().Contain(r => r.Id == reqB.Id);
        resultB.Should().NotContain(r => r.Id == reqAOlder.Id);

        // Verify DESC ordering: newer should appear before older
        var newerIdx = resultA.FindIndex(r => r.Id == reqANewer.Id);
        var olderIdx = resultA.FindIndex(r => r.Id == reqAOlder.Id);
        newerIdx.Should().BeGreaterThanOrEqualTo(0, "newer item should be in results");
        olderIdx.Should().BeGreaterThanOrEqualTo(0, "older item should be in results");
        newerIdx.Should().BeLessThan(olderIdx, "DESC: newer before older");
    }

    [Fact]
    public async Task GetNextPendingAsync_ShouldReturnOldestPending()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-next-user", "llm-next@example.com", "hash");
        db.Users.Add(user);

        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var first = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"first\"}");
        var second = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"second\"}");
        db.LlmRequests.AddRange(first, second);
        await db.SaveChangesAsync();

        // Set explicit timestamps for deterministic ordering
        db.Entry(first).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime;
        db.Entry(second).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime.AddSeconds(1);
        await db.SaveChangesAsync();

        // Clear the change tracker so the repository query reads fresh data from the database.
        db.ChangeTracker.Clear();

        var next = await repo.GetNextPendingAsync();

        // Should get the earliest created pending request (across all users)
        next.Should().NotBeNull();
        // We can't guarantee it's our `first` if other tests seeded data,
        // but the returned request should be pending.
        next!.Status.Should().Be(RequestStatus.Pending);
    }

    [Fact]
    public async Task GetStatusCountsByUserAsync_ShouldGroupCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-counts-user", "llm-counts@example.com", "hash");
        db.Users.Add(user);

        var p1 = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"p1\"}");
        var p2 = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"p2\"}");
        var proc = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"proc\"}");
        proc.MarkAsProcessing();
        db.LlmRequests.AddRange(p1, p2, proc);
        await db.SaveChangesAsync();

        var counts = await repo.GetStatusCountsByUserAsync(user.Id);

        counts.Should().ContainKey(RequestStatus.Pending);
        counts[RequestStatus.Pending].Should().Be(2);
        counts.Should().ContainKey(RequestStatus.Processing);
        counts[RequestStatus.Processing].Should().Be(1);
    }

    [Fact]
    public async Task TryClaimProcessingAsync_ShouldClaimPendingRequest()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-claim-pending-user", "llm-claim-pending@example.com", "hash");
        db.Users.Add(user);

        var request = new LlmRequest(user.Id, "chat.completion", "{\"text\":\"claim-pending-test\"}");
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        request.Status.Should().Be(RequestStatus.Pending);
        var expectedUpdatedAt = request.UpdatedAt;

        var result = await repo.TryClaimProcessingAsync(request.Id, expectedUpdatedAt);

        result.Should().BeTrue();

        // Re-fetch to verify status changed in the database
        db.ChangeTracker.Clear();
        var updated = await repo.GetByIdAsync(request.Id);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(RequestStatus.Processing);
    }

    [Fact]
    public async Task TryClaimProcessingAsync_ShouldFailWhenStatusAlreadyChanged()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-claim-stale-user", "llm-claim-stale@example.com", "hash");
        db.Users.Add(user);

        var request = new LlmRequest(user.Id, "chat.completion", "{\"text\":\"already-claimed\"}");
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        var expectedUpdatedAt = request.UpdatedAt;

        // Simulate another worker already claiming it by transitioning to Processing
        request.MarkAsProcessing();
        await db.SaveChangesAsync();

        // Try to claim with the old expectedUpdatedAt -- should fail
        var result = await repo.TryClaimProcessingAsync(request.Id, expectedUpdatedAt);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryClaimProcessingAsync_ShouldSucceedOnFirstClaim_FailOnSecond()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("llm-claim-race-user", "llm-claim-race@example.com", "hash");
        db.Users.Add(user);

        var request = new LlmRequest(user.Id, "chat.completion", "{\"text\":\"race-test\"}");
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        var expectedUpdatedAt = request.UpdatedAt;

        // Use separate scopes + Task.WhenAll for truly concurrent claims
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var firstRepo = firstScope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();
        var secondRepo = secondScope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var results = await Task.WhenAll(
            firstRepo.TryClaimProcessingAsync(request.Id, expectedUpdatedAt),
            secondRepo.TryClaimProcessingAsync(request.Id, expectedUpdatedAt));

        // Exactly one should succeed (optimistic concurrency)
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task TryClaimProcessingAsync_ShouldRejectNonPendingRequest()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-nonpending-user", "llm-nonpending@example.com", "hash");
        db.Users.Add(user);

        // Request is already Processing — TryClaimProcessingAsync should reject
        var request = new LlmRequest(user.Id, "chat.completion", "{\"text\":\"not-pending\"}");
        request.MarkAsProcessing();
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        var result = await repo.TryClaimProcessingAsync(request.Id, request.UpdatedAt);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryClaimProcessingAsync_ShouldRefreshTrackedEntity()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-claim-refresh-user", "llm-claim-refresh@example.com", "hash");
        db.Users.Add(user);

        var request = new LlmRequest(user.Id, "chat.completion", "{\"text\":\"refresh-tracked\"}");
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        // Mimic the service path: fetch the candidate via a tracking query so the
        // entity is held by the change tracker as Pending before the raw-SQL claim.
        var tracked = (await repo.GetByStatusAsync(RequestStatus.Pending))
            .Single(r => r.Id == request.Id);
        tracked.Status.Should().Be(RequestStatus.Pending);
        var expectedUpdatedAt = tracked.UpdatedAt;

        var claimed = await repo.TryClaimProcessingAsync(request.Id, expectedUpdatedAt);

        claimed.Should().BeTrue();

        // The raw-SQL UPDATE bypasses the change tracker; the repository must refresh
        // the tracked instance so callers holding it observe the claimed state.
        tracked.Status.Should().Be(RequestStatus.Processing);
        tracked.UpdatedAt.Should().NotBe(expectedUpdatedAt);

        // Read the persisted row from a fresh, untracked query and assert the tracked
        // instance now mirrors the DB exactly. This distinguishes a true DB reload
        // (ReloadAsync) from an in-memory MarkAsProcessing() substitute that would set
        // a different UTC-now UpdatedAt than the value the raw-SQL UPDATE persisted.
        using var freshScope = _factory.Services.CreateScope();
        var freshDb = freshScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var persisted = await freshDb.LlmRequests
            .AsNoTracking()
            .SingleAsync(r => r.Id == request.Id);
        persisted.Status.Should().Be(RequestStatus.Processing);
        tracked.UpdatedAt.Should().Be(persisted.UpdatedAt);
    }

    [Fact]
    public async Task TryClaimProcessingAsync_GetByIdAfterClaim_ShouldReturnProcessingWithoutClearingTracker()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-claim-findasync-user", "llm-claim-findasync@example.com", "hash");
        db.Users.Add(user);

        var request = new LlmRequest(user.Id, "chat.completion", "{\"text\":\"findasync-after-claim\"}");
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        var claimed = await repo.TryClaimProcessingAsync(request.Id, request.UpdatedAt);

        claimed.Should().BeTrue();

        // Deliberately do NOT clear the change tracker: GetByIdAsync delegates to
        // FindAsync, which serves the tracked instance from the identity map.
        // Without a post-claim refresh it would still report stale Pending.
        var refetched = await repo.GetByIdAsync(request.Id);
        refetched.Should().NotBeNull();
        refetched!.Status.Should().Be(RequestStatus.Processing);
    }

    [Fact]
    public async Task GuidFormat_ShouldBePreservedThroughRoundTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-guid-user", "llm-guid@example.com", "hash");
        db.Users.Add(user);

        var request = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"guid-test\"}");
        var originalId = request.Id;
        db.LlmRequests.Add(request);
        await db.SaveChangesAsync();

        var retrieved = await repo.GetByIdAsync(originalId);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(originalId);
        retrieved.UserId.Should().Be(user.Id);
    }

    /// <summary>
    /// Runs <paramref name="body"/> against a fresh, isolated SQLite database with NO web host (and thus
    /// no background worker), so tests that seed worker-claimable rows are deterministic. Each call uses a
    /// unique temp file and migrates the real schema, then cleans up the db/wal/shm/journal files.
    /// </summary>
    private static async Task WithSqliteRepoAsync(
        Func<TaskdeckDbContext, LlmQueueRepository, Task> body,
        DbCommandInterceptor? interceptor = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-llmqueue-bound-{Guid.NewGuid():N}.db");
        try
        {
            var builder = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite($"Data Source={dbPath}");
            if (interceptor != null)
            {
                builder.AddInterceptors(interceptor);
            }

            await using var db = new TaskdeckDbContext(builder.Options);
            await db.Database.MigrateAsync();
            await body(db, new LlmQueueRepository(db));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (!File.Exists(path))
                {
                    continue;
                }

                try { File.Delete(path); }
                catch (IOException) { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>Captures the SQL text of every reader command, so tests can assert LIMIT pushdown.</summary>
    private sealed class CapturingReaderInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyCollection<string> CapturedCommands => _commands;

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
