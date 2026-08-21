using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
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

    [SkippableFact]
    public async Task ActiveWorkloadQueries_ExcludeArchivedArtifacts_BeforeListAndCount()
    {
        SkipIfDockerUnavailable();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var user = new User($"active-workload-pg-{suffix}", $"active-workload-pg-{suffix}@example.com", "hash");
        var otherUser = new User($"active-workload-pg-other-{suffix}", $"active-workload-pg-other-{suffix}@example.com", "hash");
        var activeBoard = new Board("PostgreSQL active board", ownerId: user.Id);
        var archivedBoard = new Board("PostgreSQL archived board", ownerId: user.Id);
        archivedBoard.Archive();
        Db.AddRange(user, otherUser, activeBoard, archivedBoard);
        await Db.SaveChangesAsync();

        static string Payload(string text) => CaptureRequestContract.SerializePayload(
            new CapturePayloadV1(CaptureRequestContract.CurrentSchemaVersion, CaptureSource.Typed, text));

        static void AddProvenance(
            LlmRequest capture,
            string text,
            Guid? boardId = null,
            Guid? proposalId = null)
        {
            capture.UpdatePayload(CaptureRequestContract.SerializePayload(
                CaptureRequestContract.WithProvenance(
                    new CapturePayloadV1(
                        CaptureRequestContract.CurrentSchemaVersion,
                        CaptureSource.Typed,
                        text),
                    capture.Id,
                    proposalId: proposalId,
                    boardId: boardId)));
        }

        var activeCapture = new LlmRequest(
            user.Id,
            CaptureRequestContract.RequestTypeV1,
            Payload("PostgreSQL active capture"),
            activeBoard.Id);
        var archivedDirectCapture = new LlmRequest(
            user.Id,
            CaptureRequestContract.RequestTypeV1,
            Payload("PostgreSQL archived direct capture"),
            archivedBoard.Id);
        var archivedProvenanceCapture = new LlmRequest(
            user.Id,
            CaptureRequestContract.RequestTypeV1,
            Payload("PostgreSQL archived provenance capture"));
        AddProvenance(
            archivedProvenanceCapture,
            "PostgreSQL archived provenance capture",
            boardId: archivedBoard.Id);

        var archivedAppliedCapture = new LlmRequest(
            user.Id,
            CaptureRequestContract.RequestTypeV1,
            Payload("PostgreSQL archived applied capture"));
        archivedAppliedCapture.MarkAsProcessing();
        var appliedProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            user.Id,
            "PostgreSQL applied proposal board resolution",
            RiskLevel.Low,
            $"corr-active-workload-applied-{suffix}",
            archivedBoard.Id,
            archivedAppliedCapture.Id.ToString());
        appliedProposal.Approve(user.Id);
        appliedProposal.MarkAsApplied();
        AddProvenance(
            archivedAppliedCapture,
            "PostgreSQL archived applied capture",
            proposalId: appliedProposal.Id);

        AutomationProposal Pending(Guid requestedByUserId, string summary, Guid? boardId = null) =>
            new(
                ProposalSourceType.Queue,
                requestedByUserId,
                summary,
                RiskLevel.Low,
                $"corr-active-workload-{Guid.NewGuid():N}",
                boardId);

        var activeProposal = Pending(user.Id, "PostgreSQL active pending", activeBoard.Id);
        var archivedProposal = Pending(user.Id, "PostgreSQL archived pending", archivedBoard.Id);
        var boardlessProposal = Pending(user.Id, "PostgreSQL boardless pending");
        var danglingProposal = Pending(user.Id, "PostgreSQL dangling pending", Guid.NewGuid());
        var snoozedProposal = Pending(user.Id, "PostgreSQL snoozed pending", activeBoard.Id);
        snoozedProposal.Defer(TimeSpan.FromMinutes(30));
        var otherUsersProposal = Pending(otherUser.Id, "PostgreSQL other user's pending", activeBoard.Id);

        Db.AddRange(
            activeCapture,
            archivedDirectCapture,
            archivedProvenanceCapture,
            archivedAppliedCapture,
            appliedProposal,
            activeProposal,
            archivedProposal,
            boardlessProposal,
            danglingProposal,
            snoozedProposal,
            otherUsersProposal);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var captureRepository = new LlmQueueRepository(Db);
        var proposalRepository = new AutomationProposalRepository(Db);
        var captureSummary = await captureRepository.GetCaptureSummaryByUserAsync(user.Id);
        var activeReview = (await proposalRepository.GetActiveByUserIdAsync(
                user.Id,
                limit: 100,
                status: ProposalStatus.PendingReview))
            .ToList();
        var pendingReviewCount = await proposalRepository.CountPendingReviewByUserIdAsync(user.Id);

        captureSummary.TotalCaptures.Should().Be(4);
        captureSummary.NewCount.Should().Be(1);
        captureSummary.TriagingCount.Should().Be(0);
        activeReview.Select(proposal => proposal.Id).Should().BeEquivalentTo(
            [activeProposal.Id, boardlessProposal.Id, danglingProposal.Id]);
        pendingReviewCount.Should().Be(activeReview.Count);
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
