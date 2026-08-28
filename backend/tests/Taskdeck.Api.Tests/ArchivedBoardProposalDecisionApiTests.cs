using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArchivedBoardProposalDecisionApiTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ArchivedDecisionMessage =
        "Cannot modify proposals on an archived board. Restore the board before changing its decision history.";

    private readonly TestWebApplicationFactory _factory;

    public ArchivedBoardProposalDecisionApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("defer")]
    [InlineData("revision")]
    [InlineData("execute")]
    [InlineData("dismiss")]
    public async Task DecisionWrite_OnArchivedBoard_ReturnsExactConflict_AndKeepsLedgerUnchanged(
        string action)
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, $"archived-{action}");
        var board = await ApiTestHarness.CreateBoardAsync(client, $"archived-{action}-board");
        var proposal = await CreateProposalAsync(client, user.UserId, board.Id);

        if (action == "execute")
        {
            var approveResponse = await client.PostAsync(
                $"/api/automation/proposals/{proposal.Id}/approve",
                content: null);
            approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        else if (action == "dismiss")
        {
            var rejectResponse = await client.PostAsJsonAsync(
                $"/api/automation/proposals/{proposal.Id}/reject",
                new UpdateProposalStatusDto("Prepared for archived dismiss proof"));
            rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await ArchiveBoardAsync(board.Id);
        var before = await ReadLedgerAsync(proposal.Id, board.Id);

        var response = await SendDecisionAsync(client, action, proposal.Id, board.Id);

        await AssertArchivedDecisionConflictAsync(response);
        var after = await ReadLedgerAsync(proposal.Id, board.Id);
        after.Should().Be(before);
    }

    [Fact]
    public async Task Dismiss_MixedActiveAndArchivedBoards_IsAtomic()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archived-dismiss-batch");
        var activeBoard = await ApiTestHarness.CreateBoardAsync(client, "active-dismiss-board");
        var archivedBoard = await ApiTestHarness.CreateBoardAsync(client, "archived-dismiss-board");
        var activeProposal = await CreateProposalAsync(client, user.UserId, activeBoard.Id);
        var archivedProposal = await CreateProposalAsync(client, user.UserId, archivedBoard.Id);

        foreach (var proposal in new[] { activeProposal, archivedProposal })
        {
            var rejectResponse = await client.PostAsJsonAsync(
                $"/api/automation/proposals/{proposal.Id}/reject",
                new UpdateProposalStatusDto("Prepared for atomic dismiss proof"));
            rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await ArchiveBoardAsync(archivedBoard.Id);
        var activeBefore = await ReadLedgerAsync(activeProposal.Id, activeBoard.Id);
        var archivedBefore = await ReadLedgerAsync(archivedProposal.Id, archivedBoard.Id);

        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals/dismiss",
            new { ids = new[] { activeProposal.Id, archivedProposal.Id } });

        await AssertArchivedDecisionConflictAsync(response);
        (await ReadLedgerAsync(activeProposal.Id, activeBoard.Id)).Should().Be(activeBefore);
        (await ReadLedgerAsync(archivedProposal.Id, archivedBoard.Id)).Should().Be(archivedBefore);
    }

    [Fact]
    public async Task Execute_OperationThatArchivesBoard_CommitsApplied_AndIdempotentReplayStaysUnguarded()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "execute-archives-board");
        var board = await ApiTestHarness.CreateBoardAsync(client, "execute-archives-board");
        var createResponse = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Chat,
                user.UserId,
                "Archive board through reviewed proposal",
                RiskLevel.Medium,
                Guid.NewGuid().ToString("N"),
                board.Id,
                Operations: new List<CreateProposalOperationDto>
                {
                    new(
                        0,
                        "update",
                        "board",
                        JsonSerializer.Serialize(new { boardId = board.Id, isArchived = true }),
                        Guid.NewGuid().ToString("N"),
                        board.Id.ToString())
                }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var proposal = await createResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        (await client.PostAsync(
            $"/api/automation/proposals/{proposal!.Id}/approve",
            content: null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var firstExecute = await SendExecuteAsync(client, proposal.Id);
        firstExecute.StatusCode.Should().Be(HttpStatusCode.OK);
        var applied = await firstExecute.Content.ReadFromJsonAsync<ProposalDto>();
        applied!.Status.Should().Be(ProposalStatus.Applied);

        var replay = await SendExecuteAsync(client, proposal.Id);
        replay.StatusCode.Should().Be(HttpStatusCode.OK,
            "already-Applied execute performs no decision write and remains idempotent on an archived board");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Boards.AsNoTracking().SingleAsync(entity => entity.Id == board.Id))
            .IsArchived.Should().BeTrue();
        (await db.AutomationProposals.AsNoTracking().SingleAsync(entity => entity.Id == proposal.Id))
            .Status.Should().Be(ProposalStatus.Applied);
    }

    private static async Task<ProposalDto> CreateProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Chat,
                userId,
                $"Archived decision proof {Guid.NewGuid():N}",
                RiskLevel.Low,
                Guid.NewGuid().ToString("N"),
                boardId,
                Operations: new List<CreateProposalOperationDto>
                {
                    new CreateProposalOperationDto(
                        0,
                        "update",
                        "board",
                        JsonSerializer.Serialize(new
                        {
                            boardId,
                            name = $"Should not apply {Guid.NewGuid():N}"
                        }),
                        Guid.NewGuid().ToString("N"),
                        boardId.ToString())
                }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private async Task ArchiveBoardAsync(Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var board = await db.Boards.SingleAsync(entity => entity.Id == boardId);
        board.Archive();
        await db.SaveChangesAsync();
    }

    private async Task<DecisionLedgerSnapshot> ReadLedgerAsync(Guid proposalId, Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var proposal = await db.AutomationProposals
            .AsNoTracking()
            .SingleAsync(entity => entity.Id == proposalId);
        var board = await db.Boards
            .AsNoTracking()
            .SingleAsync(entity => entity.Id == boardId);

        return new DecisionLedgerSnapshot(
            proposal.Status,
            proposal.DecidedAt,
            proposal.DecidedByUserId,
            proposal.AppliedAt,
            proposal.FailureReason,
            proposal.DeferredUntil,
            proposal.ApprovedRevisionId,
            proposal.UpdatedAt,
            await db.ProposalRevisions.CountAsync(revision => revision.ProposalId == proposalId),
            await db.ProposalOutcomes.CountAsync(outcome => outcome.ProposalId == proposalId),
            board.Name,
            await db.Cards.CountAsync(card => card.BoardId == boardId));
    }

    private static async Task<HttpResponseMessage> SendDecisionAsync(
        HttpClient client,
        string action,
        Guid proposalId,
        Guid boardId)
    {
        switch (action)
        {
            case "approve":
                return await client.PostAsync($"/api/automation/proposals/{proposalId}/approve", content: null);
            case "reject":
                return await client.PostAsJsonAsync(
                    $"/api/automation/proposals/{proposalId}/reject",
                    new UpdateProposalStatusDto("Archived reject must not land"));
            case "defer":
                return await client.PostAsJsonAsync(
                    $"/api/automation/proposals/{proposalId}/defer",
                    new DeferProposalRequestDto(10));
            case "revision":
                return await client.PostAsJsonAsync(
                    $"/api/automation/proposals/{proposalId}/revisions",
                    new
                    {
                        revisedPayload = BuildRevisionPayload(proposalId, boardId),
                        reason = "Archived revision must not land"
                    });
            case "execute":
                return await SendExecuteAsync(client, proposalId);
            case "dismiss":
                return await client.PostAsJsonAsync(
                    "/api/automation/proposals/dismiss",
                    new { ids = new[] { proposalId } });
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown decision action");
        }
    }

    private static async Task<HttpResponseMessage> SendExecuteAsync(HttpClient client, Guid proposalId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/automation/proposals/{proposalId}/execute");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static string BuildRevisionPayload(Guid proposalId, Guid boardId)
    {
        return JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    proposalId,
                    sequence = 0,
                    actionType = "update",
                    targetType = "board",
                    targetId = boardId.ToString(),
                    parameters = JsonSerializer.Serialize(new
                    {
                        boardId,
                        name = $"Archived revision {Guid.NewGuid():N}"
                    }),
                    idempotencyKey = Guid.NewGuid().ToString("N"),
                    expectedVersion = (string?)null
                }
            }
        });
    }

    private static async Task AssertArchivedDecisionConflictAsync(HttpResponseMessage response)
    {
        await ApiTestHarness.AssertErrorContractAsync(
            response,
            HttpStatusCode.Conflict,
            "InvalidOperation");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("message").GetString().Should().Be(ArchivedDecisionMessage);
    }

    private sealed record DecisionLedgerSnapshot(
        ProposalStatus Status,
        DateTime? DecidedAt,
        Guid? DecidedByUserId,
        DateTime? AppliedAt,
        string? FailureReason,
        DateTime? DeferredUntil,
        Guid? ApprovedRevisionId,
        DateTimeOffset UpdatedAt,
        int RevisionCount,
        int OutcomeCount,
        string BoardName,
        int CardCount);
}
