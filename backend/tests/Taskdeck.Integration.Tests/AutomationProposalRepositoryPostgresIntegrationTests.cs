using FluentAssertions;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Repositories;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

[Collection(PostgresTestCollection.Name)]
public class AutomationProposalRepositoryPostgresIntegrationTests : PostgresIntegrationTestBase
{
    public AutomationProposalRepositoryPostgresIntegrationTests(PostgresContainerFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task GetTerminalByActionTypeAsync_TiedCutoff_UsesIdBeforeTake()
    {
        SkipIfDockerUnavailable();

        var user = new User("similar-postgres", "similar-postgres@example.com", "hash");
        var board = new Board("Similar-past PostgreSQL board", ownerId: user.Id);
        Db.Users.Add(user);
        Db.Boards.Add(board);
        await Db.SaveChangesAsync();

        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var first = CreateAppliedProposal(firstId, user.Id, board.Id, "First deterministic row");
        var second = CreateAppliedProposal(secondId, user.Id, board.Id, "Second deterministic row");
        var third = CreateAppliedProposal(thirdId, user.Id, board.Id, "Third deterministic row");

        var tiedDecision = DateTime.UtcNow.AddMinutes(-1);
        var tiedUpdate = new DateTimeOffset(tiedDecision);
        // Persist each row separately in reverse Id order. Without Id in SQL's ORDER BY before
        // LIMIT, PostgreSQL's tied-row cohort follows this heap arrival order on the regression path.
        foreach (var proposal in new[] { third, second, first })
        {
            Db.Entry(proposal).Property(nameof(AutomationProposal.DecidedAt)).CurrentValue = tiedDecision;
            Db.Entry(proposal).Property(nameof(AutomationProposal.UpdatedAt)).CurrentValue = tiedUpdate;
            Db.AutomationProposals.Add(proposal);
            await Db.SaveChangesAsync();
        }
        Db.ChangeTracker.Clear();

        var repository = new AutomationProposalRepository(Db);
        var result = await repository.GetTerminalByActionTypeAsync(
            "create",
            board.Id,
            user.Id,
            limit: 2);

        result.Select(proposal => proposal.Id).Should().Equal(firstId, secondId);
    }

    private static AutomationProposal CreateAppliedProposal(
        Guid id,
        Guid userId,
        Guid boardId,
        string summary)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            summary,
            RiskLevel.Low,
            $"corr-postgres-{id:N}",
            boardId: boardId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(proposal, id);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "create",
            "card",
            "{\"title\":\"History card\"}",
            $"key-postgres-{id:N}"));
        proposal.Approve(userId);
        proposal.MarkAsApplied();
        return proposal;
    }
}
