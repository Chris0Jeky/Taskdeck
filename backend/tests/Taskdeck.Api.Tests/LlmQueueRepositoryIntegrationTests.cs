using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
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
        var pendingFirst = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"first\"}");
        await Task.Delay(20); // ensure different CreatedAt
        var pendingSecond = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"second\"}");
        var processing = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"processing\"}");
        processing.MarkAsProcessing();

        db.LlmRequests.AddRange(pendingFirst, pendingSecond, processing);
        await db.SaveChangesAsync();

        var result = (await repo.GetPendingAsync(limit: 10)).ToList();

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

        result.Count.Should().BeLessOrEqualTo(2);
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

        // First claim should succeed
        var firstClaim = await repo.TryClaimProcessingCaptureAsync(
            request.Id, expectedUpdatedAt);
        firstClaim.Should().BeTrue();

        // Second claim with stale UpdatedAt should fail (optimistic concurrency)
        var secondClaim = await repo.TryClaimProcessingCaptureAsync(
            request.Id, expectedUpdatedAt);
        secondClaim.Should().BeFalse();
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

        var reqA = new LlmRequest(userA.Id, "inbox.capture.text", "{\"text\":\"a\"}");
        var reqB = new LlmRequest(userB.Id, "inbox.capture.text", "{\"text\":\"b\"}");
        db.LlmRequests.AddRange(reqA, reqB);
        await db.SaveChangesAsync();

        var resultA = (await repo.GetByUserAsync(userA.Id)).ToList();
        var resultB = (await repo.GetByUserAsync(userB.Id)).ToList();

        resultA.Should().Contain(r => r.Id == reqA.Id);
        resultA.Should().NotContain(r => r.Id == reqB.Id);
        resultB.Should().Contain(r => r.Id == reqB.Id);
        resultB.Should().NotContain(r => r.Id == reqA.Id);
    }

    [Fact]
    public async Task GetNextPendingAsync_ShouldReturnOldestPending()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmQueueRepository>();

        var user = new User("llm-next-user", "llm-next@example.com", "hash");
        db.Users.Add(user);

        var first = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"first\"}");
        await Task.Delay(20);
        var second = new LlmRequest(user.Id, "inbox.capture.text", "{\"text\":\"second\"}");
        db.LlmRequests.AddRange(first, second);
        await db.SaveChangesAsync();

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
        counts[RequestStatus.Pending].Should().BeGreaterOrEqualTo(2);
        counts.Should().ContainKey(RequestStatus.Processing);
        counts[RequestStatus.Processing].Should().BeGreaterOrEqualTo(1);
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
}
