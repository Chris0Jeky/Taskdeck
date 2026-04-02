using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for AutomationProposalRepository against real SQLite.
/// Covers ordering correctness, expiry boundary, status filtering, and operation-target lookups.
/// </summary>
public class AutomationProposalRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AutomationProposalRepositoryIntegrationTests(TestWebApplicationFactory factory)
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
    }

    [Fact]
    public async Task GetExpiredAsync_ShouldReturnOnlyExpiredPendingReview()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("ap-expiry-user", "ap-expiry@example.com", "hash");
        db.Users.Add(user);

        // Short expiry that will be expired immediately
        var expired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Will expire soon", RiskLevel.Low,
            $"corr-exp-{Guid.NewGuid():N}", expiryMinutes: 0);

        // Long expiry that should NOT appear
        var notExpired = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Not expired yet", RiskLevel.Low,
            $"corr-notexp-{Guid.NewGuid():N}", expiryMinutes: 60);

        db.AutomationProposals.AddRange(expired, notExpired);
        await db.SaveChangesAsync();

        // Wait briefly to ensure expiryMinutes: 0 means ExpiresAt is in the past
        await Task.Delay(50);

        var results = (await repo.GetExpiredAsync()).ToList();

        // The 0-minute proposal should be expired by now (ExpiresAt = UtcNow at creation)
        results.Should().Contain(p => p.Id == expired.Id);
        results.Should().NotContain(p => p.Id == notExpired.Id);
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

        count.Should().BeGreaterOrEqualTo(2);
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
}
