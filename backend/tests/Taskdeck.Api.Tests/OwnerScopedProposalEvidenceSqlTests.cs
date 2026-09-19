using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Real-SQL authorization regressions for #3249. Board-less proposals intentionally use the
/// requesting-user scope rather than the target card's board. Each test includes a newer or
/// otherwise actionable proposal from another owner so a missing owner predicate cannot pass
/// through a positive-only fixture.
/// </summary>
public sealed class OwnerScopedProposalEvidenceSqlTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OwnerScopedProposalEvidenceSqlTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PendingOwnerScope_ExcludesAnotherOwnersBoardlessProposal()
    {
        using var ownerClient = _factory.CreateClient();
        using var strangerClient = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, $"owner-pending-{suffix}");
        var stranger = await ApiTestHarness.AuthenticateAsync(strangerClient, $"stranger-pending-{suffix}");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, $"owner-pending-{suffix}");

        Guid currentId;
        Guid targetCardId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var columnId = await db.Columns
                .Where(column => column.BoardId == boardId)
                .Select(column => column.Id)
                .SingleAsync();
            var target = new Card(boardId, columnId, "Owner-scoped pending target");
            var current = Proposal(owner.UserId, boardId: null, "Current board-less proposal", target.Id);
            var strangerCandidate = Proposal(
                stranger.UserId,
                boardId: null,
                "Another owner's board-less pending proposal",
                target.Id);

            db.AddRange(target, current, strangerCandidate);
            await db.SaveChangesAsync();
            currentId = current.Id;
            targetCardId = target.Id;
        }

        var withoutOwnerCandidate = await ownerClient.GetFromJsonAsync<List<ConflictRowDto>>(
            $"/api/automation/proposals/{currentId}/conflicts");
        withoutOwnerCandidate.Should().NotContain(row => row.Key == "duplicate-proposal",
            "a board-less proposal from another owner must not cross the requesting-user scope");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.AutomationProposals.Add(Proposal(
                owner.UserId,
                boardId: null,
                "Same owner's board-less pending proposal",
                targetCardId));
            await db.SaveChangesAsync();
        }

        var withOwnerCandidate = await ownerClient.GetFromJsonAsync<List<ConflictRowDto>>(
            $"/api/automation/proposals/{currentId}/conflicts");
        withOwnerCandidate.Should().Contain(row => row.Key == "duplicate-proposal",
            "the owner-scoped pending SQL arm must still discover the caller's own collision");
    }

    [Fact]
    public async Task HistoryOwnerScope_ReturnsTheOwnersLatestRelatedProposalOnly()
    {
        using var ownerClient = _factory.CreateClient();
        using var strangerClient = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, $"owner-history-{suffix}");
        var stranger = await ApiTestHarness.AuthenticateAsync(strangerClient, $"stranger-history-{suffix}");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, $"owner-history-{suffix}");

        Guid currentId;
        const string ownerSummary = "Owner-scoped related history";
        const string strangerSummary = "Cross-owner history must stay hidden";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var columnId = await db.Columns
                .Where(column => column.BoardId == boardId)
                .Select(column => column.Id)
                .SingleAsync();
            var target = new Card(boardId, columnId, "Owner-scoped history target");
            var current = Proposal(owner.UserId, boardId: null, "Current board-less history proposal", target.Id);
            var ownerCandidate = Proposal(owner.UserId, boardId: null, ownerSummary, target.Id);
            var strangerCandidate = Proposal(stranger.UserId, boardId: null, strangerSummary, target.Id);
            SetUpdatedAt(ownerCandidate, DateTimeOffset.UtcNow.AddMinutes(-10));
            SetUpdatedAt(strangerCandidate, DateTimeOffset.UtcNow.AddMinutes(10));

            db.AddRange(target, current, ownerCandidate, strangerCandidate);
            await db.SaveChangesAsync();
            currentId = current.Id;
        }

        var history = await ownerClient.GetFromJsonAsync<List<CardHistoryRowDto>>(
            $"/api/automation/proposals/{currentId}/history");

        history.Should().Contain(row => row.Event.Contains(ownerSummary, StringComparison.Ordinal),
            "the owner-scoped history SQL arm must find the caller's related proposal");
        history.Should().NotContain(row => row.Event.Contains(strangerSummary, StringComparison.Ordinal),
            "even a newer board-less proposal from another owner is outside the evidence scope");
    }

    [Fact]
    public async Task TerminalOwnerScope_ExcludesAnotherOwnersAppliedDecision()
    {
        using var ownerClient = _factory.CreateClient();
        using var strangerClient = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, $"owner-terminal-{suffix}");
        var stranger = await ApiTestHarness.AuthenticateAsync(strangerClient, $"stranger-terminal-{suffix}");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, $"owner-terminal-{suffix}");

        Guid currentId;
        const string ownerSummary = "Owner-scoped applied decision";
        const string strangerSummary = "Cross-owner applied decision must stay hidden";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var columnId = await db.Columns
                .Where(column => column.BoardId == boardId)
                .Select(column => column.Id)
                .SingleAsync();
            var target = new Card(boardId, columnId, "Owner-scoped terminal target");
            var current = Proposal(owner.UserId, boardId: null, "Current board-less terminal proposal", target.Id);
            var ownerCandidate = Proposal(owner.UserId, boardId: null, ownerSummary, target.Id);
            ownerCandidate.Approve(owner.UserId);
            ownerCandidate.MarkAsApplied();
            var strangerCandidate = Proposal(stranger.UserId, boardId: null, strangerSummary, target.Id);
            strangerCandidate.Approve(stranger.UserId);
            strangerCandidate.MarkAsApplied();

            db.AddRange(target, current, ownerCandidate, strangerCandidate);
            await db.SaveChangesAsync();
            currentId = current.Id;
        }

        var similar = await ownerClient.GetFromJsonAsync<SimilarPastResultDto>(
            $"/api/automation/proposals/{currentId}/similar-past");

        similar.Should().NotBeNull();
        similar!.Decisions.Select(decision => decision.Title).Should().Contain(ownerSummary,
            "the owner-scoped terminal SQL arm must return the caller's matching decision");
        similar.Decisions.Select(decision => decision.Title).Should().NotContain(strangerSummary,
            "terminal evidence must never cross the requesting-user boundary for board-less proposals");
    }

    private static AutomationProposal Proposal(
        Guid userId,
        Guid? boardId,
        string summary,
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
            actionType: "update",
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

    private static void SetUpdatedAt(Entity entity, DateTimeOffset value)
    {
        var property = typeof(Entity).GetProperty(
            nameof(Entity.UpdatedAt),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.SetValue(entity, value);
    }
}
