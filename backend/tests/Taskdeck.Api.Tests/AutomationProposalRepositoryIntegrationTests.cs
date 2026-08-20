using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for AutomationProposalRepository against real SQLite.
/// Covers ordering correctness, expiry boundary, status filtering, and operation-target lookups.
///
/// Uses <see cref="HostedWorkerDisabledTestWebApplicationFactory"/> (issue #1335): the expiry
/// tests backdate ExpiresAt on PendingReview proposals — exactly the rows the live
/// <c>ProposalHousekeepingWorker</c> (runs immediately at host start, then every 60s) transitions
/// PendingReview→Expired. Without worker isolation the sweep can consume a seeded proposal
/// between seed and the GetExpiredAsync read.
/// </summary>
public class AutomationProposalRepositoryIntegrationTests : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    private readonly HostedWorkerDisabledTestWebApplicationFactory _factory;

    public AutomationProposalRepositoryIntegrationTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetByStatusAsync_ShouldReturnOnlyMatchingStatus_WithOperations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-status-user", "ap-status@example.com", "hash");
        db.Users.Add(user);

        var pending = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Pending proposal", RiskLevel.Low,
            $"corr-pending-{Guid.NewGuid():N}");
        // Add an operation to verify the "WithOperations" part of the test name
        var operation = new AutomationProposalOperation(
            pending.Id, 0, "create", "card", "{\"title\":\"Test Card\"}",
            $"idempkey-{Guid.NewGuid():N}", targetId: Guid.NewGuid().ToString());
        pending.AddOperation(operation);

        var approved = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Approved proposal", RiskLevel.Low,
            $"corr-approved-{Guid.NewGuid():N}");
        approved.Approve(user.Id);

        db.AutomationProposals.AddRange(pending, approved);
        await db.SaveChangesAsync();

        var pendingResults = (await repo.GetByStatusAsync(ProposalStatus.PendingReview)).ToList();
        var approvedResults = (await repo.GetByStatusAsync(ProposalStatus.Approved)).ToList();

        pendingResults.Should().Contain(p => p.Id == pending.Id);
        pendingResults.Should().NotContain(p => p.Id == approved.Id);
        approvedResults.Should().Contain(p => p.Id == approved.Id);
        approvedResults.Should().NotContain(p => p.Id == pending.Id);

        // Verify operations are included in the result
        var loadedPending = pendingResults.First(p => p.Id == pending.Id);
        loadedPending.Operations.Should().HaveCount(1);
        loadedPending.Operations[0].ActionType.Should().Be("create");
    }

    [Fact]
    public async Task GetExpiredAsync_ShouldReturnOnlyExpiredPendingReview()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-expiry-user", "ap-expiry@example.com", "hash");
        db.Users.Add(user);

        // Create with minimum valid expiry, then force ExpiresAt to the past before saving.
        var expired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Will expire soon", RiskLevel.Low,
            $"corr-exp-{Guid.NewGuid():N}", expiryMinutes: 1);
        SetExpiresAt(expired, DateTime.UtcNow.AddDays(-1));

        // Long expiry that should NOT appear
        var notExpired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Not expired yet", RiskLevel.Low,
            $"corr-notexp-{Guid.NewGuid():N}", expiryMinutes: 60);

        // Approved AND past ExpiresAt: must be EXCLUDED by the Status == PendingReview filter (a decided
        // proposal is never re-expired). Approve while still fresh, then backdate ExpiresAt. #1259 makes the
        // worker rely on this query, so the status filter is asserted directly.
        var approvedExpired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Approved then expired", RiskLevel.Low,
            $"corr-apprexp-{Guid.NewGuid():N}", expiryMinutes: 60);
        approvedExpired.Approve(user.Id);
        SetExpiresAt(approvedExpired, DateTime.UtcNow.AddDays(-1));

        db.AutomationProposals.AddRange(expired, notExpired, approvedExpired);
        await db.SaveChangesAsync();

        // Clear the change tracker so the subsequent query reads fresh data from the database
        // rather than serving the stale tracked entity with the old ExpiresAt value.
        db.ChangeTracker.Clear();

        var results = (await repo.GetExpiredAsync()).ToList();

        results.Should().Contain(p => p.Id == expired.Id);
        results.Should().NotContain(p => p.Id == notExpired.Id);
        results.Should().NotContain(p => p.Id == approvedExpired.Id,
            "GetExpiredAsync only returns PendingReview proposals; a decided (Approved) one is never re-expired");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldIncludeOperations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-ops-user", "ap-ops@example.com", "hash");
        db.Users.Add(user);

        var proposal = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "With operations", RiskLevel.Medium,
            $"corr-ops-{Guid.NewGuid():N}");
        var operation = new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", "{\"title\":\"New Card\"}",
            $"idempkey-{Guid.NewGuid():N}", targetId: Guid.NewGuid().ToString());
        proposal.AddOperation(operation);

        db.AutomationProposals.Add(proposal);
        await db.SaveChangesAsync();

        var result = await repo.GetByIdAsync(proposal.Id);

        result.Should().NotBeNull();
        result!.Operations.Should().HaveCount(1);
        result.Operations[0].ActionType.Should().Be("create");
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_ShouldFindExactMatch()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-corr-user", "ap-corr@example.com", "hash");
        db.Users.Add(user);

        var correlationId = $"unique-correlation-{Guid.NewGuid():N}";
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat, user.Id, "Corr test", RiskLevel.Low,
            correlationId);
        db.AutomationProposals.Add(proposal);
        await db.SaveChangesAsync();

        var result = await repo.GetByCorrelationIdAsync(correlationId);
        result.Should().NotBeNull();
        result!.Id.Should().Be(proposal.Id);

        var noMatch = await repo.GetByCorrelationIdAsync("nonexistent-correlation");
        noMatch.Should().BeNull();
    }

    [Fact]
    public async Task GetByUserIdAsync_ShouldIsolateByUser()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var userA = new User("ap-usera", "ap-usera@example.com", "hash");
        var userB = new User("ap-userb", "ap-userb@example.com", "hash");
        db.Users.AddRange(userA, userB);

        var proposalA = new AutomationProposal(
            ProposalSourceType.Queue, userA.Id, "User A proposal", RiskLevel.Low,
            $"corr-a-{Guid.NewGuid():N}");
        var proposalB = new AutomationProposal(
            ProposalSourceType.Queue, userB.Id, "User B proposal", RiskLevel.Low,
            $"corr-b-{Guid.NewGuid():N}");
        db.AutomationProposals.AddRange(proposalA, proposalB);
        await db.SaveChangesAsync();

        var resultsA = (await repo.GetByUserIdAsync(userA.Id)).ToList();
        var resultsB = (await repo.GetByUserIdAsync(userB.Id)).ToList();

        resultsA.Should().Contain(p => p.Id == proposalA.Id);
        resultsA.Should().NotContain(p => p.Id == proposalB.Id);
        resultsB.Should().Contain(p => p.Id == proposalB.Id);
        resultsB.Should().NotContain(p => p.Id == proposalA.Id);
    }

    [Fact]
    public async Task GetByUserIdAsync_ResurfacedDeferredProposal_DoesNotEvictFresherPendingFromFullPage()
    {
        // #1247: the bounded top-N selection must order by CreatedAt (the display order), not the
        // defer-inflated ExpiresAt. A resurfaced deferred proposal (ADR-0042 pushed its ExpiresAt far out)
        // must not occupy a top slot and push a fresher pending proposal out of a full page.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-defer-order", "ap-defer-order@example.com", "hash");
        db.Users.Add(user);

        var now = DateTime.UtcNow;
        var nowOffset = DateTimeOffset.UtcNow;

        // OLD proposal that was deferred and has resurfaced (DeferredUntil in the past -> passes the
        // visibility filter), but whose ExpiresAt is inflated far into the future.
        var resurfacedDeferred = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Old resurfaced deferred", RiskLevel.Low,
            $"corr-deferred-{Guid.NewGuid():N}");
        var fresh1 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Fresh pending 1", RiskLevel.Low,
            $"corr-fresh1-{Guid.NewGuid():N}");
        var fresh2 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Fresh pending 2", RiskLevel.Low,
            $"corr-fresh2-{Guid.NewGuid():N}");
        db.AutomationProposals.AddRange(resurfacedDeferred, fresh1, fresh2);

        // The deferred proposal is the OLDEST by CreatedAt; the two fresh ones are newer.
        db.Entry(resurfacedDeferred).Property("CreatedAt").CurrentValue = nowOffset.AddDays(-3);
        db.Entry(fresh1).Property("CreatedAt").CurrentValue = nowOffset.AddHours(-2);
        db.Entry(fresh2).Property("CreatedAt").CurrentValue = nowOffset.AddHours(-1);

        // The deferred proposal has the HIGHEST ExpiresAt (would win under the old ExpiresAt ordering);
        // the fresh ones carry a normal ~24h TTL.
        SetExpiresAt(resurfacedDeferred, now.AddDays(10));
        SetExpiresAt(fresh1, now.AddHours(22));
        SetExpiresAt(fresh2, now.AddHours(23));
        SetDeferredUntil(resurfacedDeferred, now.AddHours(-1)); // resurfaced: snooze has elapsed

        await db.SaveChangesAsync();

        // A full page of 2 from 3 candidates must return the two FRESHEST by CreatedAt, not the inflated
        // deferred one.
        var page = (await repo.GetByUserIdAsync(user.Id, limit: 2)).ToList();

        page.Should().HaveCount(2);
        page.Select(p => p.Id).Should().BeEquivalentTo(new[] { fresh1.Id, fresh2.Id });
        page.Should().NotContain(p => p.Id == resurfacedDeferred.Id,
            "the bounded window orders by CreatedAt, so a resurfaced deferred proposal cannot evict a fresher pending one");
    }

    [Fact]
    public async Task GetByUserIdAsync_EqualCreatedAt_SelectsDeterministicallyViaIdTiebreaker()
    {
        // #1247 (Id tiebreaker): proposals created in the same batch share CreatedAt to the tick. Without a
        // secondary sort key the bounded-window boundary is nondeterministic; with ORDER BY CreatedAt, Id the
        // same query returns the same row every time.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-tie", "ap-tie@example.com", "hash");
        db.Users.Add(user);

        var sharedCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var p1 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Tie 1", RiskLevel.Low, $"corr-tie1-{Guid.NewGuid():N}");
        var p2 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Tie 2", RiskLevel.Low, $"corr-tie2-{Guid.NewGuid():N}");
        db.AutomationProposals.AddRange(p1, p2);
        db.Entry(p1).Property("CreatedAt").CurrentValue = sharedCreatedAt;
        db.Entry(p2).Property("CreatedAt").CurrentValue = sharedCreatedAt; // identical CreatedAt
        await db.SaveChangesAsync();

        var first = (await repo.GetByUserIdAsync(user.Id, limit: 1)).Single();
        var second = (await repo.GetByUserIdAsync(user.Id, limit: 1)).Single();

        first.Id.Should().Be(second.Id,
            "the Id tiebreaker makes the bounded top-N deterministic when two proposals share a CreatedAt");
    }

    [Fact]
    public async Task GetByUserIdAsync_HidesSnoozedByDefault_ButIncludesThemWhenIncludeDeferred()
    {
        // C1 (#1245 review): the snooze filter must hide a deferred proposal from review-queue
        // reads (the default) yet NOT drop it from the GDPR data export, which opts in with
        // includeDeferred:true so a snoozed proposal is never silently missing from the export.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-export-snooze", "ap-export-snooze@example.com", "hash");
        db.Users.Add(user);

        var live = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Live pending", RiskLevel.Low,
            $"corr-live-{Guid.NewGuid():N}");
        var snoozed = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Snoozed pending", RiskLevel.Low,
            $"corr-snooze-{Guid.NewGuid():N}");
        snoozed.Defer(TimeSpan.FromMinutes(60)); // DeferredUntil in the future
        db.AutomationProposals.AddRange(live, snoozed);
        await db.SaveChangesAsync();

        // Default review-queue read hides the snoozed proposal.
        var queueView = (await repo.GetByUserIdAsync(user.Id)).ToList();
        queueView.Should().Contain(p => p.Id == live.Id);
        queueView.Should().NotContain(p => p.Id == snoozed.Id);

        // The completeness-sensitive export read still includes it.
        var exportView = (await repo.GetByUserIdAsync(user.Id, includeDeferred: true)).ToList();
        exportView.Should().Contain(p => p.Id == live.Id);
        exportView.Should().Contain(p => p.Id == snoozed.Id);
    }

    [Fact]
    public async Task GetByBoardIdAsync_ShouldFilterByBoard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-board-user", "ap-board@example.com", "hash");
        db.Users.Add(user);

        var boardA = new Board("Board A for proposals", ownerId: user.Id);
        var boardB = new Board("Board B for proposals", ownerId: user.Id);
        db.Boards.AddRange(boardA, boardB);

        var proposalOnA = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "On board A", RiskLevel.Low,
            $"corr-ba-{Guid.NewGuid():N}", boardId: boardA.Id);
        var proposalOnB = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "On board B", RiskLevel.Low,
            $"corr-bb-{Guid.NewGuid():N}", boardId: boardB.Id);
        db.AutomationProposals.AddRange(proposalOnA, proposalOnB);
        await db.SaveChangesAsync();

        var resultsA = (await repo.GetByBoardIdAsync(boardA.Id)).ToList();

        resultsA.Should().Contain(p => p.Id == proposalOnA.Id);
        resultsA.Should().NotContain(p => p.Id == proposalOnB.Id);
    }

    [Fact]
    public async Task GetByRiskLevelAsync_ShouldFilterCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-risk-user", "ap-risk@example.com", "hash");
        db.Users.Add(user);

        var low = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Low risk", RiskLevel.Low,
            $"corr-low-{Guid.NewGuid():N}");
        var critical = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Critical risk", RiskLevel.Critical,
            $"corr-crit-{Guid.NewGuid():N}");
        db.AutomationProposals.AddRange(low, critical);
        await db.SaveChangesAsync();

        var lowResults = (await repo.GetByRiskLevelAsync(RiskLevel.Low)).ToList();
        var critResults = (await repo.GetByRiskLevelAsync(RiskLevel.Critical)).ToList();

        lowResults.Should().Contain(p => p.Id == low.Id);
        lowResults.Should().NotContain(p => p.Id == critical.Id);
        critResults.Should().Contain(p => p.Id == critical.Id);
        critResults.Should().NotContain(p => p.Id == low.Id);
    }

    [Fact]
    public async Task CountPendingReviewByUserIdAsync_ShouldCountOnlyPendingReview()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-count-user", "ap-count@example.com", "hash");
        db.Users.Add(user);

        var pending1 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Pending one", RiskLevel.Low,
            $"corr-c1-{Guid.NewGuid():N}");
        var pending2 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Pending two", RiskLevel.Low,
            $"corr-c2-{Guid.NewGuid():N}");
        var approved = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Approved already", RiskLevel.Low,
            $"corr-c3-{Guid.NewGuid():N}");
        approved.Approve(user.Id);

        db.AutomationProposals.AddRange(pending1, pending2, approved);
        await db.SaveChangesAsync();

        var count = await repo.CountPendingReviewByUserIdAsync(user.Id);

        count.Should().Be(2);
    }

    [Fact]
    public async Task HasReviewedByUserIdAsync_ShouldDetectReviewedDecision()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var reviewer = new User("ap-reviewer", "ap-reviewer@example.com", "hash");
        var noReviewer = new User("ap-noreview", "ap-noreview@example.com", "hash");
        db.Users.AddRange(reviewer, noReviewer);

        var proposal = new AutomationProposal(
            ProposalSourceType.Queue, reviewer.Id, "Review test", RiskLevel.Low,
            $"corr-rev-{Guid.NewGuid():N}");
        proposal.Approve(reviewer.Id);
        db.AutomationProposals.Add(proposal);
        await db.SaveChangesAsync();

        var hasReviewed = await repo.HasReviewedByUserIdAsync(reviewer.Id);
        hasReviewed.Should().BeTrue();

        var hasNotReviewed = await repo.HasReviewedByUserIdAsync(noReviewer.Id);
        hasNotReviewed.Should().BeFalse();
    }

    [Fact]
    public async Task HasAppliedByUserIdAsync_ShouldOnlyCountAppliedProposals()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var applier = new User("ap-applier", "ap-applier@example.com", "hash");
        var reviewerOnly = new User("ap-reviewonly", "ap-reviewonly@example.com", "hash");
        db.Users.AddRange(applier, reviewerOnly);

        // Applier: approved AND applied — the full capture→review→apply loop.
        var appliedProposal = new AutomationProposal(
            ProposalSourceType.Queue, applier.Id, "Applied test", RiskLevel.Low,
            $"corr-app-{Guid.NewGuid():N}");
        appliedProposal.Approve(applier.Id);
        appliedProposal.MarkAsApplied();
        db.AutomationProposals.Add(appliedProposal);

        // Reviewer-only: approved but never applied — must NOT satisfy the apply milestone.
        var approvedOnly = new AutomationProposal(
            ProposalSourceType.Queue, reviewerOnly.Id, "Approved only", RiskLevel.Low,
            $"corr-appr-{Guid.NewGuid():N}");
        approvedOnly.Approve(reviewerOnly.Id);
        db.AutomationProposals.Add(approvedOnly);

        // Rejected proposal by the applier is a review, not an apply.
        var rejected = new AutomationProposal(
            ProposalSourceType.Queue, applier.Id, "Rejected", RiskLevel.Low,
            $"corr-rej-{Guid.NewGuid():N}");
        rejected.Reject(applier.Id);
        db.AutomationProposals.Add(rejected);

        await db.SaveChangesAsync();

        (await repo.HasAppliedByUserIdAsync(applier.Id)).Should().BeTrue();
        (await repo.HasAppliedByUserIdAsync(reviewerOnly.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task GetLatestByOperationTargetAsync_ShouldFindByTargetTypeAndId()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-target-user", "ap-target@example.com", "hash");
        db.Users.Add(user);

        var targetId = Guid.NewGuid().ToString();
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Target lookup", RiskLevel.Low,
            $"corr-tgt-{Guid.NewGuid():N}");
        var operation = new AutomationProposalOperation(
            proposal.Id, 0, "update", "card", "{\"title\":\"Updated\"}", $"key-{Guid.NewGuid():N}",
            targetId: targetId);
        proposal.AddOperation(operation);
        db.AutomationProposals.Add(proposal);
        await db.SaveChangesAsync();

        var result = await repo.GetLatestByOperationTargetAsync("card", targetId);
        result.Should().NotBeNull();
        result!.Id.Should().Be(proposal.Id);

        var noMatch = await repo.GetLatestByOperationTargetAsync("card", Guid.NewGuid().ToString());
        noMatch.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdsAsync_WithEmptyList_ShouldReturnEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var result = await repo.GetByIdsAsync(Array.Empty<Guid>());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdsAsync_ShouldReturnOnlyRequestedProposals()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-ids-user", "ap-ids@example.com", "hash");
        db.Users.Add(user);

        var p1 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "First batch", RiskLevel.Low,
            $"corr-id1-{Guid.NewGuid():N}");
        var p2 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Second batch", RiskLevel.Low,
            $"corr-id2-{Guid.NewGuid():N}");
        var p3 = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Not requested", RiskLevel.Low,
            $"corr-id3-{Guid.NewGuid():N}");
        db.AutomationProposals.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        var result = await repo.GetByIdsAsync(new[] { p1.Id, p2.Id });

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Id == p1.Id);
        result.Should().Contain(p => p.Id == p2.Id);
        result.Should().NotContain(p => p.Id == p3.Id);
    }

    [Fact]
    public async Task GetTerminalByActionTypeAsync_OnSqlite_WithNoTerminalHistory_ReturnsEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User(
            $"ap-similar-past-{Guid.NewGuid():N}",
            $"ap-similar-past-{Guid.NewGuid():N}@example.com",
            "hash");
        var board = new Board("Similar-past SQLite board", ownerId: user.Id);
        var pending = new AutomationProposal(
            ProposalSourceType.Queue,
            user.Id,
            "Pending capture proposal",
            RiskLevel.Low,
            $"corr-similar-{Guid.NewGuid():N}",
            boardId: board.Id);
        pending.AddOperation(new AutomationProposalOperation(
            pending.Id,
            0,
            "create",
            "card",
            "{\"title\":\"Captured card\"}",
            $"key-similar-{Guid.NewGuid():N}"));

        db.Users.Add(user);
        db.Boards.Add(board);
        db.AutomationProposals.Add(pending);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await repo.GetTerminalByActionTypeAsync(
            "create",
            board.Id,
            user.Id,
            limit: 200);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTerminalByActionTypeAsync_OnSqlite_BoardScope_ReturnsBoundedTerminalHistory()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var owner = new User(
            $"ap-history-owner-{Guid.NewGuid():N}",
            $"ap-history-owner-{Guid.NewGuid():N}@example.com",
            "hash");
        var collaborator = new User(
            $"ap-history-collab-{Guid.NewGuid():N}",
            $"ap-history-collab-{Guid.NewGuid():N}@example.com",
            "hash");
        var board = new Board("Similar-past scoped board", ownerId: owner.Id);
        var otherBoard = new Board("Similar-past other board", ownerId: owner.Id);
        db.Users.AddRange(owner, collaborator);
        db.Boards.AddRange(board, otherBoard);

        var oldestApplied = CreateTerminalProposal(owner.Id, board.Id, "Old applied", "create", ProposalStatus.Applied);
        var middleRejected = CreateTerminalProposal(owner.Id, board.Id, "Middle rejected", "create", ProposalStatus.Rejected);
        var newestCollaboratorApplied = CreateTerminalProposal(collaborator.Id, board.Id, "Newest collaborator applied", "create", ProposalStatus.Applied);
        var otherBoardApplied = CreateTerminalProposal(owner.Id, otherBoard.Id, "Other board", "create", ProposalStatus.Applied);
        var wrongActionApplied = CreateTerminalProposal(owner.Id, board.Id, "Wrong action", "move", ProposalStatus.Applied);
        db.AutomationProposals.AddRange(
            oldestApplied,
            middleRejected,
            newestCollaboratorApplied,
            otherBoardApplied,
            wrongActionApplied);

        var now = DateTime.UtcNow;
        SetDecisionTimestamps(db, oldestApplied, now.AddMinutes(-3));
        SetDecisionTimestamps(db, middleRejected, now.AddMinutes(-2));
        SetDecisionTimestamps(db, newestCollaboratorApplied, now.AddMinutes(-1));
        SetDecisionTimestamps(db, otherBoardApplied, now);
        SetDecisionTimestamps(db, wrongActionApplied, now);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await repo.GetTerminalByActionTypeAsync(
            "create",
            board.Id,
            owner.Id,
            limit: 2);

        result.Select(proposal => proposal.Id).Should().Equal(
            newestCollaboratorApplied.Id,
            middleRejected.Id);
        result.Should().OnlyContain(proposal =>
            proposal.BoardId == board.Id &&
            proposal.Operations.Any(operation => operation.ActionType == "create"));
        result.Should().NotContain(proposal => proposal.Id == oldestApplied.Id, "the database-side limit is two");
        result.Should().NotContain(proposal => proposal.Id == otherBoardApplied.Id);
        result.Should().NotContain(proposal => proposal.Id == wrongActionApplied.Id);
    }

    [Fact]
    public async Task GetTerminalByActionTypeAsync_OnSqlite_WithoutBoard_ScopesToRequestingUser()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var caller = new User(
            $"ap-history-caller-{Guid.NewGuid():N}",
            $"ap-history-caller-{Guid.NewGuid():N}@example.com",
            "hash");
        var otherUser = new User(
            $"ap-history-other-{Guid.NewGuid():N}",
            $"ap-history-other-{Guid.NewGuid():N}@example.com",
            "hash");
        db.Users.AddRange(caller, otherUser);

        var callerApplied = CreateTerminalProposal(caller.Id, null, "Caller applied", "create", ProposalStatus.Applied);
        var callerRejected = CreateTerminalProposal(caller.Id, null, "Caller rejected", "create", ProposalStatus.Rejected);
        var otherApplied = CreateTerminalProposal(otherUser.Id, null, "Other applied", "create", ProposalStatus.Applied);
        db.AutomationProposals.AddRange(callerApplied, callerRejected, otherApplied);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await repo.GetTerminalByActionTypeAsync(
            "create",
            boardId: null,
            userId: caller.Id,
            limit: 10);

        result.Select(proposal => proposal.Id).Should().BeEquivalentTo(
            new[] { callerApplied.Id, callerRejected.Id });
        result.Should().OnlyContain(proposal => proposal.RequestedByUserId == caller.Id);
        result.Should().NotContain(proposal => proposal.Id == otherApplied.Id);
    }

    [Fact]
    public async Task GetExpiredAsync_WithExpiresAtInTheFuture_ShouldNotReturnAsExpired()
    {
        // PROVES: GetExpiredAsync's ExpiresAt filter excludes an unexpired PendingReview row, and the
        // exclusion is not vacuous — a past-ExpiresAt sibling seeded alongside it IS returned by the
        // same call.
        //
        // DOES NOT PROVE: the exact strict-'<' vs '<=' behaviour at ExpiresAt == now. The repository
        // reads DateTime.UtcNow inside the query and there is no clock seam to freeze it, so no test can
        // place a row exactly on the queried "now". An earlier version of this test approximated that
        // with a one-second future offset and raced the write plus the sweep against it; on a slow
        // windows-latest runner the row was genuinely expired by query time and the assertion failed
        // legitimately (#1822). The offsets below are far wider than any plausible sweep duration.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-boundary-user", "ap-boundary@example.com", "hash");
        db.Users.Add(user);

        var unexpired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Boundary test", RiskLevel.Low,
            $"corr-boundary-{Guid.NewGuid():N}", expiryMinutes: 1);
        SetExpiresAt(unexpired, DateTime.UtcNow.AddMinutes(30));

        // Positive control: without it a GetExpiredAsync that returned nothing at all would still pass.
        var expired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Boundary control", RiskLevel.Low,
            $"corr-boundary-control-{Guid.NewGuid():N}", expiryMinutes: 1);
        SetExpiresAt(expired, DateTime.UtcNow.AddMinutes(-30));

        db.AutomationProposals.AddRange(unexpired, expired);
        await db.SaveChangesAsync();

        // Clear the change tracker so the subsequent query materializes fresh rows from the database.
        db.ChangeTracker.Clear();

        var results = (await repo.GetExpiredAsync()).ToList();

        results.Should().NotContain(p => p.Id == unexpired.Id, "ExpiresAt is 30 minutes in the future");
        results.Should().Contain(p => p.Id == expired.Id, "ExpiresAt is 30 minutes in the past");
    }

    [Fact]
    public async Task GetPendingByOperationTargetAsync_NormalizesGuidAndTargetType_ExcludesExpired()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User($"ap-target-user-{Guid.NewGuid():N}", $"ap-target-{Guid.NewGuid():N}@example.com", "hash");
        db.Users.Add(user);

        var targetId = Guid.NewGuid();
        var pending = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Pending target", RiskLevel.Low,
            $"corr-pending-target-{Guid.NewGuid():N}");
        pending.AddOperation(new AutomationProposalOperation(
            pending.Id, 0, "update", "Card", "{\"title\":\"Updated\"}",
            $"key-pending-{Guid.NewGuid():N}", targetId: targetId.ToString("B")));

        var expired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Expired target", RiskLevel.Low,
            $"corr-expired-target-{Guid.NewGuid():N}");
        expired.AddOperation(new AutomationProposalOperation(
            expired.Id, 0, "update", "card", "{\"title\":\"Expired\"}",
            $"key-expired-{Guid.NewGuid():N}", targetId: targetId.ToString("D").ToUpperInvariant()));
        SetExpiresAt(expired, DateTime.UtcNow.AddMinutes(-1));

        db.AutomationProposals.AddRange(pending, expired);
        await db.SaveChangesAsync();

        var results = await repo.GetPendingByOperationTargetAsync(" CARD ", targetId.ToString("N").ToUpperInvariant());

        results.Should().ContainSingle(p => p.Id == pending.Id);
        results.Should().NotContain(p => p.Id == expired.Id);
    }

    [Fact]
    public async Task GetByStatusAsync_PendingReview_ExcludesSnoozed_AndReincludesAfterWindow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User($"ap-defer-{Guid.NewGuid():N}", $"ap-defer-{Guid.NewGuid():N}@example.com", "hash");
        db.Users.Add(user);

        var snoozed = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Snoozed pending", RiskLevel.Low,
            $"corr-snz-{Guid.NewGuid():N}");
        snoozed.Defer(TimeSpan.FromMinutes(60)); // DeferredUntil in the future

        var live = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Live pending", RiskLevel.Low,
            $"corr-live-{Guid.NewGuid():N}");

        // A snoozed proposal whose ExpiresAt was forced into the past must STILL stay hidden
        // (DeferredUntil>now wins): a snoozed proposal never resurfaces just because it expired.
        var snoozedButExpired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Snoozed but forced-expired", RiskLevel.Low,
            $"corr-snzexp-{Guid.NewGuid():N}");
        snoozedButExpired.Defer(TimeSpan.FromMinutes(60));
        SetExpiresAt(snoozedButExpired, DateTime.UtcNow.AddMinutes(-5));

        db.AutomationProposals.AddRange(snoozed, live, snoozedButExpired);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var whileSnoozed = (await repo.GetByStatusAsync(ProposalStatus.PendingReview)).ToList();
        whileSnoozed.Should().Contain(p => p.Id == live.Id);
        whileSnoozed.Should().NotContain(p => p.Id == snoozed.Id);
        whileSnoozed.Should().NotContain(p => p.Id == snoozedButExpired.Id);

        // Elapse the snooze window for `snoozed`: it must re-enter the pending queue.
        SetDeferredUntil(snoozed, DateTime.UtcNow.AddMinutes(-1));
        db.AutomationProposals.Update(snoozed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var afterWindow = (await repo.GetByStatusAsync(ProposalStatus.PendingReview)).ToList();
        afterWindow.Should().Contain(p => p.Id == snoozed.Id);
    }

    [Fact]
    public async Task GetByStatusAsync_Approved_StillReturnsDecidedProposal_WithStaleDeferredUntil()
    {
        // Regression for Fix A: the list filter is status-gated, so a decided proposal that
        // somehow retained a future DeferredUntil is never hidden.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User($"ap-stale-{Guid.NewGuid():N}", $"ap-stale-{Guid.NewGuid():N}@example.com", "hash");
        db.Users.Add(user);

        var approved = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Approved with stale snooze", RiskLevel.Low,
            $"corr-stale-{Guid.NewGuid():N}");
        approved.Approve(user.Id);
        // Force a stale residual snooze that should NOT hide a decided proposal.
        SetDeferredUntil(approved, DateTime.UtcNow.AddMinutes(60));

        db.AutomationProposals.Add(approved);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var approvedResults = (await repo.GetByStatusAsync(ProposalStatus.Approved)).ToList();
        approvedResults.Should().Contain(p => p.Id == approved.Id);
    }

    [Fact]
    public async Task GetByIdAsync_StillReturnsDeferredProposal()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User($"ap-defid-{Guid.NewGuid():N}", $"ap-defid-{Guid.NewGuid():N}@example.com", "hash");
        db.Users.Add(user);

        var snoozed = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Snoozed but fetchable", RiskLevel.Low,
            $"corr-defid-{Guid.NewGuid():N}");
        snoozed.Defer(TimeSpan.FromMinutes(60));
        db.AutomationProposals.Add(snoozed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // A snoozed proposal must still be reachable by id / deep-link.
        var result = await repo.GetByIdAsync(snoozed.Id);
        result.Should().NotBeNull();
        result!.DeferredUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task CountPendingReviewByUserIdAsync_ExcludesSnoozed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User($"ap-defcount-{Guid.NewGuid():N}", $"ap-defcount-{Guid.NewGuid():N}@example.com", "hash");
        db.Users.Add(user);

        var live = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Live pending", RiskLevel.Low,
            $"corr-cl-{Guid.NewGuid():N}");
        var snoozed = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Snoozed pending", RiskLevel.Low,
            $"corr-cs-{Guid.NewGuid():N}");
        snoozed.Defer(TimeSpan.FromMinutes(60));

        db.AutomationProposals.AddRange(live, snoozed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // The badge count must match the visible queue: snoozed is excluded.
        var count = await repo.CountPendingReviewByUserIdAsync(user.Id);
        count.Should().Be(1);
    }

    [Fact]
    public async Task GetPendingByOperationTargetAsync_StillReturnsDeferredProposal()
    {
        // Fix D: a snoozed pending change still claims its target card, so conflict detection
        // must keep seeing it.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User($"ap-deftgt-{Guid.NewGuid():N}", $"ap-deftgt-{Guid.NewGuid():N}@example.com", "hash");
        db.Users.Add(user);

        var targetId = Guid.NewGuid();
        var snoozed = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Snoozed claims target", RiskLevel.Low,
            $"corr-deftgt-{Guid.NewGuid():N}");
        snoozed.AddOperation(new AutomationProposalOperation(
            snoozed.Id, 0, "update", "card", "{\"title\":\"Updated\"}",
            $"key-deftgt-{Guid.NewGuid():N}", targetId: targetId.ToString()));
        snoozed.Defer(TimeSpan.FromMinutes(60));

        db.AutomationProposals.Add(snoozed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var results = await repo.GetPendingByOperationTargetAsync("card", targetId.ToString());
        results.Should().Contain(p => p.Id == snoozed.Id);
    }

    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        typeof(AutomationProposal)
            .GetProperty(nameof(AutomationProposal.ExpiresAt))!
            .SetValue(proposal, expiresAt);
    }

    private static void SetDeferredUntil(AutomationProposal proposal, DateTime deferredUntil)
    {
        typeof(AutomationProposal)
            .GetProperty(nameof(AutomationProposal.DeferredUntil))!
            .SetValue(proposal, deferredUntil);
    }

    private static AutomationProposal CreateTerminalProposal(
        Guid userId,
        Guid? boardId,
        string summary,
        string actionType,
        ProposalStatus status)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            summary,
            RiskLevel.Low,
            $"corr-history-{Guid.NewGuid():N}",
            boardId: boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            actionType,
            "card",
            "{\"title\":\"History card\"}",
            $"key-history-{Guid.NewGuid():N}"));

        if (status == ProposalStatus.Applied)
        {
            proposal.Approve(userId);
            proposal.MarkAsApplied();
        }
        else if (status == ProposalStatus.Rejected)
        {
            proposal.Reject(userId);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Expected Applied or Rejected.");
        }

        return proposal;
    }

    private static void SetDecisionTimestamps(
        TaskdeckDbContext db,
        AutomationProposal proposal,
        DateTime decidedAt)
    {
        db.Entry(proposal).Property(nameof(AutomationProposal.DecidedAt)).CurrentValue = decidedAt;
        db.Entry(proposal).Property(nameof(AutomationProposal.UpdatedAt)).CurrentValue = new DateTimeOffset(decidedAt);
    }
}
