using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

/// <summary>
/// Integration tests for the AutomationProposal lifecycle against an ephemeral
/// PostgreSQL container. Validates that proposals can be created, approved,
/// rejected, applied, and expired with correct state transitions persisted
/// to a real relational database.
/// </summary>
[Collection(PostgresTestCollection.Name)]
public class ProposalLifecycleIntegrationTests : PostgresIntegrationTestBase
{
    public ProposalLifecycleIntegrationTests(PostgresContainerFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task CreateProposal_WithOperations_ShouldPersist()
    {
        var user = new User("proposal-user1", "proposal1@example.com", "hash123");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            user.Id,
            "Create a new card for the backlog",
            RiskLevel.Low,
            $"corr-{Guid.NewGuid():N}");

        var operation = new AutomationProposalOperation(
            proposal.Id, 0, "create", "card",
            "{\"title\":\"New Card\",\"description\":\"Automatically suggested\"}",
            $"idem-{Guid.NewGuid():N}");
        proposal.AddOperation(operation);

        Db.AutomationProposals.Add(proposal);
        await Db.SaveChangesAsync();

        var retrieved = await Db.AutomationProposals
            .Include(p => p.Operations)
            .FirstOrDefaultAsync(p => p.Id == proposal.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Summary.Should().Be("Create a new card for the backlog");
        retrieved.Status.Should().Be(ProposalStatus.PendingReview);
        retrieved.RiskLevel.Should().Be(RiskLevel.Low);
        retrieved.Operations.Should().HaveCount(1);
        retrieved.Operations[0].ActionType.Should().Be("create");
        retrieved.Operations[0].TargetType.Should().Be("card");
    }

    [Fact]
    public async Task ApproveProposal_ShouldPersistApprovedState()
    {
        var user = new User("proposal-user2", "proposal2@example.com", "hash123");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            user.Id,
            "Move card to Done column",
            RiskLevel.Medium,
            $"corr-{Guid.NewGuid():N}");

        Db.AutomationProposals.Add(proposal);
        await Db.SaveChangesAsync();

        proposal.Approve(user.Id);
        await Db.SaveChangesAsync();

        Db.Entry(proposal).State = EntityState.Detached;
        var reloaded = await Db.AutomationProposals.FindAsync(proposal.Id);

        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(ProposalStatus.Approved);
        reloaded.DecidedByUserId.Should().Be(user.Id);
        reloaded.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectProposal_ShouldPersistRejectedState()
    {
        var user = new User("proposal-user3", "proposal3@example.com", "hash123");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        var proposal = new AutomationProposal(
            ProposalSourceType.Manual,
            user.Id,
            "Delete all cards from board",
            RiskLevel.High,
            $"corr-{Guid.NewGuid():N}");

        Db.AutomationProposals.Add(proposal);
        await Db.SaveChangesAsync();

        proposal.Reject(user.Id, "Too destructive");
        await Db.SaveChangesAsync();

        Db.Entry(proposal).State = EntityState.Detached;
        var reloaded = await Db.AutomationProposals.FindAsync(proposal.Id);

        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(ProposalStatus.Rejected);
        reloaded.FailureReason.Should().Be("Too destructive");
    }

    [Fact]
    public async Task ApproveAndApply_ShouldCompleteFullLifecycle()
    {
        var user = new User("proposal-user4", "proposal4@example.com", "hash123");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            user.Id,
            "Rename board to Updated Name",
            RiskLevel.Low,
            $"corr-{Guid.NewGuid():N}");

        var operation = new AutomationProposalOperation(
            proposal.Id, 0, "update", "board",
            "{\"name\":\"Updated Name\"}",
            $"idem-{Guid.NewGuid():N}");
        proposal.AddOperation(operation);

        Db.AutomationProposals.Add(proposal);
        await Db.SaveChangesAsync();

        // Approve
        proposal.Approve(user.Id);
        await Db.SaveChangesAsync();

        // Apply
        proposal.MarkAsApplied();
        await Db.SaveChangesAsync();

        Db.Entry(proposal).State = EntityState.Detached;
        var reloaded = await Db.AutomationProposals
            .Include(p => p.Operations)
            .FirstOrDefaultAsync(p => p.Id == proposal.Id);

        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(ProposalStatus.Applied);
        reloaded.AppliedAt.Should().NotBeNull();
        reloaded.Operations.Should().HaveCount(1);
    }

    [Fact]
    public async Task FilterProposals_ByStatus_ShouldReturnCorrectSubset()
    {
        var user = new User("proposal-user5", "proposal5@example.com", "hash123");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        var pending = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Pending proposal",
            RiskLevel.Low, $"corr-{Guid.NewGuid():N}");

        var approved = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Approved proposal",
            RiskLevel.Low, $"corr-{Guid.NewGuid():N}");
        approved.Approve(user.Id);

        var rejected = new AutomationProposal(
            ProposalSourceType.Queue, user.Id, "Rejected proposal",
            RiskLevel.Low, $"corr-{Guid.NewGuid():N}");
        rejected.Reject(user.Id);

        Db.AutomationProposals.AddRange(pending, approved, rejected);
        await Db.SaveChangesAsync();

        var pendingResults = await Db.AutomationProposals
            .Where(p => p.Status == ProposalStatus.PendingReview)
            .ToListAsync();

        var approvedResults = await Db.AutomationProposals
            .Where(p => p.Status == ProposalStatus.Approved)
            .ToListAsync();

        pendingResults.Should().Contain(p => p.Id == pending.Id);
        pendingResults.Should().NotContain(p => p.Id == approved.Id);
        approvedResults.Should().Contain(p => p.Id == approved.Id);
        approvedResults.Should().NotContain(p => p.Id == pending.Id);
    }
}
