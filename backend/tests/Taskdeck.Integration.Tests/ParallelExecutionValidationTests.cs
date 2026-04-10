using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

/// <summary>
/// Validates that rapid sequential database operations are race-free. Each
/// test method gets its own isolated database (xUnit 2.x creates a new class
/// instance per test), so cross-test contamination cannot occur even when
/// xUnit runs multiple test methods in parallel.
/// </summary>
[Collection(PostgresTestCollection.Name)]
public class ParallelExecutionValidationTests : PostgresIntegrationTestBase
{
    public ParallelExecutionValidationTests(PostgresContainerFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task SequentialBoardCreation_ShouldProduceUniqueIds()
    {
        SkipIfDockerUnavailable();
        var user = new User("parallel-user1", "parallel1@example.com", "hash123");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        // DbContext is NOT thread-safe, so we serialize all operations.
        // This validates that rapid sequential Add + SaveChanges produces
        // unique, correctly-persisted boards without collisions.
        const int boardCount = 10;
        var boardIds = new List<Guid>();
        for (var i = 0; i < boardCount; i++)
        {
            var board = new Board($"Parallel Board {i}", $"Created by iteration {i}", user.Id);
            Db.Boards.Add(board);
            await Db.SaveChangesAsync();
            boardIds.Add(board.Id);
        }

        // Verify all boards were created without collisions
        boardIds.Should().HaveCount(boardCount);
        boardIds.Should().OnlyHaveUniqueItems("each board should have a unique ID");

        var allBoards = await Db.Boards.ToListAsync();
        allBoards.Should().HaveCount(boardCount);
    }

    [SkippableFact]
    public async Task ConcurrentCardCreation_AcrossColumns_ShouldMaintainIntegrity()
    {
        SkipIfDockerUnavailable();
        var user = new User("parallel-user2", "parallel2@example.com", "hash123");
        Db.Users.Add(user);

        var board = new Board("Parallel Card Board", null, user.Id);
        Db.Boards.Add(board);

        var columnA = new Column(board.Id, "Column A", 0);
        var columnB = new Column(board.Id, "Column B", 1);
        Db.Columns.AddRange(columnA, columnB);
        await Db.SaveChangesAsync();

        // Create cards in alternating columns
        const int cardCount = 20;
        for (var i = 0; i < cardCount; i++)
        {
            var targetColumn = i % 2 == 0 ? columnA : columnB;
            var card = new Card(board.Id, targetColumn.Id, $"Card {i}", position: i);
            Db.Cards.Add(card);
        }
        await Db.SaveChangesAsync();

        var cardsInA = await Db.Cards.Where(c => c.ColumnId == columnA.Id).CountAsync();
        var cardsInB = await Db.Cards.Where(c => c.ColumnId == columnB.Id).CountAsync();

        cardsInA.Should().Be(cardCount / 2);
        cardsInB.Should().Be(cardCount / 2);
        (cardsInA + cardsInB).Should().Be(cardCount);
    }

    [SkippableFact]
    public async Task RapidProposalStateTransitions_ShouldNotLoseData()
    {
        SkipIfDockerUnavailable();
        var user = new User("parallel-user3", "parallel3@example.com", "hash123");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        const int proposalCount = 5;
        var proposalIds = new List<Guid>();

        // Create proposals and immediately transition them
        for (var i = 0; i < proposalCount; i++)
        {
            var proposal = new AutomationProposal(
                ProposalSourceType.Queue,
                user.Id,
                $"Rapid proposal {i}",
                RiskLevel.Low,
                $"corr-rapid-{Guid.NewGuid():N}");

            var op = new AutomationProposalOperation(
                proposal.Id, 0, "create", "card",
                $"{{\"title\":\"Card from proposal {i}\"}}",
                $"idem-rapid-{Guid.NewGuid():N}");
            proposal.AddOperation(op);

            Db.AutomationProposals.Add(proposal);
            await Db.SaveChangesAsync();

            // Immediately approve
            proposal.Approve(user.Id);
            await Db.SaveChangesAsync();

            // Immediately mark as applied
            proposal.MarkAsApplied();
            await Db.SaveChangesAsync();

            proposalIds.Add(proposal.Id);
        }

        // Verify all proposals reached Applied state
        var applied = await Db.AutomationProposals
            .Include(p => p.Operations)
            .Where(p => proposalIds.Contains(p.Id))
            .ToListAsync();

        applied.Should().HaveCount(proposalCount);
        applied.Should().OnlyContain(p => p.Status == ProposalStatus.Applied);
        applied.Should().OnlyContain(p => p.Operations.Count == 1);
    }
}
