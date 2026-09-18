using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Real-SQL regressions for #2452. The proposal currently under review is already
/// revision-resolved by the controller (#2453); these cases prove that RELATED
/// proposals are filtered by the same effective operation set rather than their
/// immutable creation-time operation rows.
/// </summary>
public sealed class RelatedProposalRevisionEvidenceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RelatedProposalRevisionEvidenceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PendingLatestRevisionThatMovesOntoTheCard_IsVisibleToDuplicateAndHistoryEvidence()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "related-pending-include");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "related-pending-include");

        Guid currentId;
        string relatedSummary;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var columnId = await db.Columns.Where(column => column.BoardId == boardId)
                .Select(column => column.Id).SingleAsync();
            var target = new Card(boardId, columnId, "Current target");
            var oldTarget = new Card(boardId, columnId, "Original related target", position: 1);
            db.Cards.AddRange(target, oldTarget);

            var current = Proposal(user.UserId, boardId, "Current proposal", "update", target.Id);
            relatedSummary = "Pending proposal moved onto current card by revision";
            var related = Proposal(user.UserId, boardId, relatedSummary, "update", oldTarget.Id);
            var revision = Revision(related, 1, user.UserId, "update", target.Id,
                "Latest pending revision changes the target card");
            db.AutomationProposals.AddRange(current, related);
            db.ProposalRevisions.Add(revision);
            await db.SaveChangesAsync();
            currentId = current.Id;
        }

        var conflicts = await client.GetFromJsonAsync<List<ConflictRowDto>>(
            $"/api/automation/proposals/{currentId}/conflicts");
        conflicts.Should().Contain(row => row.Key == "duplicate-proposal",
            "the latest pending revision targets the same card Apply would mutate");

        var history = await client.GetFromJsonAsync<List<CardHistoryRowDto>>(
            $"/api/automation/proposals/{currentId}/history");
        history.Should().Contain(row => row.Event.Contains(relatedSummary, StringComparison.Ordinal),
            "related-card history must discover a target introduced by the latest pending revision");
    }

    [Fact]
    public async Task PendingLatestRevisionThatMovesAwayFromTheCard_IsExcludedFromDuplicateAndHistoryEvidence()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "related-pending-exclude");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "related-pending-exclude");

        Guid currentId;
        string unrelatedSummary;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var columnId = await db.Columns.Where(column => column.BoardId == boardId)
                .Select(column => column.Id).SingleAsync();
            var target = new Card(boardId, columnId, "Current target");
            var revisedTarget = new Card(boardId, columnId, "Revised-away target", position: 1);
            db.Cards.AddRange(target, revisedTarget);

            // Create the current proposal first. The related proposal's ORIGINAL target row is
            // therefore the newest raw-operation match, so the pre-fix query returns a false hit.
            var current = Proposal(user.UserId, boardId, "Current proposal", "update", target.Id);
            unrelatedSummary = "Pending proposal revised away from current card";
            var unrelated = Proposal(user.UserId, boardId, unrelatedSummary, "update", target.Id);
            var revision = Revision(unrelated, 1, user.UserId, "update", revisedTarget.Id,
                "Latest pending revision changes to another card");
            db.AutomationProposals.AddRange(current, unrelated);
            db.ProposalRevisions.Add(revision);
            await db.SaveChangesAsync();
            currentId = current.Id;
        }

        var conflicts = await client.GetFromJsonAsync<List<ConflictRowDto>>(
            $"/api/automation/proposals/{currentId}/conflicts");
        conflicts.Should().NotContain(row => row.Key == "duplicate-proposal",
            "an original target superseded by a revision is not what approval or Apply will use");

        var history = await client.GetFromJsonAsync<List<CardHistoryRowDto>>(
            $"/api/automation/proposals/{currentId}/history");
        history.Should().NotContain(row => row.Event.Contains(unrelatedSummary, StringComparison.Ordinal),
            "related-card history must not retain a creation-time target removed by revision");
    }

    [Fact]
    public async Task ApprovedPinDrivesRelatedHistoryAndSimilarPastDespiteLaterRevisionAndRawFalsePositive()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "related-approved-pin");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "related-approved-pin");

        Guid currentId;
        string pinnedSummary;
        string rejectedSummary;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var columnId = await db.Columns.Where(column => column.BoardId == boardId)
                .Select(column => column.Id).SingleAsync();
            var target = new Card(boardId, columnId, "Current target");
            var otherTarget = new Card(boardId, columnId, "Other target", position: 1);
            db.Cards.AddRange(target, otherTarget);

            var current = Proposal(user.UserId, boardId, "Current update", "update", target.Id);

            // Inclusion control: creation-time rows say move/other-card, but the approved pin says
            // update/current-card. A later unpinned revision must not change that Applied evidence.
            pinnedSummary = "Applied decision whose approved pin targets current card";
            var pinned = Proposal(user.UserId, boardId, pinnedSummary, "move", otherTarget.Id);
            var approvedRevision = Revision(pinned, 1, user.UserId, "update", target.Id,
                "Reviewer changes action and target before approval");
            pinned.Approve(user.UserId, approvedRevision.Id);
            pinned.MarkAsApplied();
            var laterUnpinnedRevision = Revision(pinned, 2, user.UserId, "archive", otherTarget.Id,
                "Post-approval revision must not replace the pin");

            // Exclusion/rate control: the original row says update/current-card and would be a
            // rejected similar decision, but the decision-time latest revision moved it away.
            rejectedSummary = "Rejected decision whose effective revision is unrelated";
            var rejected = Proposal(user.UserId, boardId, rejectedSummary, "update", target.Id);
            var rejectedRevision = Revision(rejected, 1, user.UserId, "move", otherTarget.Id,
                "Rejected decision is frozen on an unrelated revision");
            rejected.Reject(user.UserId, "Use another card");

            db.AutomationProposals.AddRange(current, pinned, rejected);
            db.ProposalRevisions.AddRange(
                approvedRevision,
                laterUnpinnedRevision,
                rejectedRevision);
            await db.SaveChangesAsync();
            currentId = current.Id;
        }

        var history = await client.GetFromJsonAsync<List<CardHistoryRowDto>>(
            $"/api/automation/proposals/{currentId}/history");
        history.Should().Contain(row => row.Event.Contains(pinnedSummary, StringComparison.Ordinal),
            "history must follow the approved operation set that Apply executed");
        history.Should().NotContain(row => row.Event.Contains(rejectedSummary, StringComparison.Ordinal),
            "a superseded original target is not related evidence");

        var similar = await client.GetFromJsonAsync<SimilarPastResultDto>(
            $"/api/automation/proposals/{currentId}/similar-past");
        similar.Should().NotBeNull();
        similar!.Decisions.Should().ContainSingle()
            .Which.Title.Should().Be(pinnedSummary);
        similar.ApplyRate.Should().Be(100,
            "only the effectively matching Applied decision belongs in the update-action cohort");
    }

    private static AutomationProposal Proposal(
        Guid userId,
        Guid boardId,
        string summary,
        string actionType,
        Guid targetCardId)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Manual,
            userId,
            summary,
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType,
            targetType: "card",
            parameters: JsonSerializer.Serialize(new
            {
                cardId = targetCardId,
                title = $"Evidence {summary}"
            }),
            idempotencyKey: Guid.NewGuid().ToString("N"),
            targetId: targetCardId.ToString("D")));
        return proposal;
    }

    private static ProposalRevision Revision(
        AutomationProposal proposal,
        int revisionNumber,
        Guid editorUserId,
        string actionType,
        Guid targetCardId,
        string reason)
    {
        var payload = JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType,
                    targetType = "card",
                    targetId = targetCardId.ToString("D"),
                    parameters = JsonSerializer.Serialize(new
                    {
                        cardId = targetCardId,
                        title = $"Revision {reason}"
                    }),
                    idempotencyKey = Guid.NewGuid().ToString("N")
                }
            }
        });
        return new ProposalRevision(
            proposal.Id,
            revisionNumber,
            editorUserId,
            payload,
            reason);
    }
}
