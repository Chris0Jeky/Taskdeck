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

public sealed class ProposalConflictReviewApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProposalConflictReviewApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetConflicts_HistoricalMalformedOperation_ReturnsIncompleteReviewWarning()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "conflicts-malformed-operation");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "conflicts-malformed-operation");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;

        var cardResponse = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, board.Columns.Single().Id, "Historical target", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created, await cardResponse.Content.ReadAsStringAsync());
        var card = (await cardResponse.Content.ReadFromJsonAsync<CardDto>())!;

        var createResponse = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Manual,
                user.UserId,
                "Historically malformed review operation",
                RiskLevel.Low,
                Guid.NewGuid().ToString(),
                boardId,
                Operations:
                [
                    new CreateProposalOperationDto(
                        0,
                        "update",
                        "card",
                        JsonSerializer.Serialize(new
                        {
                            boardId,
                            cardId = card.Id,
                            title = "Proposed title",
                            expectedUpdatedAt = card.UpdatedAt,
                        }),
                        Guid.NewGuid().ToString(),
                        card.Id.ToString()),
                ]));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        var proposal = (await createResponse.Content.ReadFromJsonAsync<ProposalDto>())!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var operation = await db.AutomationProposalOperations
                .SingleAsync(candidate => candidate.ProposalId == proposal.Id);
            db.Entry(operation).Property(candidate => candidate.Parameters).CurrentValue = "not-json{";
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/conflicts");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var conflicts = await response.Content.ReadFromJsonAsync<List<ConflictRowDto>>();

        conflicts.Should().NotBeNull();
        conflicts!.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Warn
            && row.Key == "unable-to-evaluate-operation"
            && row.Value.Contains("1", StringComparison.Ordinal));
        conflicts.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        conflicts.Should().NotContain(row =>
            row.Key == "status" && row.Value == "No conflicts detected");
        body.Should().NotContain("not-json");
    }

    [Fact]
    public async Task GetConflicts_UnsupportedIdentifierAction_ReturnsIncompleteReviewWarningWithoutPayloadLeak()
    {
        const string privatePayload = "unsupported-action-private-payload";
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "conflicts-unsupported-action");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "conflicts-unsupported-action");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;

        var cardResponse = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, board.Columns.Single().Id, "Unsupported action target", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created, await cardResponse.Content.ReadAsStringAsync());
        var card = (await cardResponse.Content.ReadFromJsonAsync<CardDto>())!;

        var createResponse = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Manual,
                user.UserId,
                "Future operation awaiting execution support",
                RiskLevel.Low,
                Guid.NewGuid().ToString(),
                boardId,
                Operations:
                [
                    new CreateProposalOperationDto(
                        0,
                        "future-card-action",
                        "card",
                        JsonSerializer.Serialize(new
                        {
                            boardId,
                            cardId = card.Id,
                            privatePayload,
                        }),
                        Guid.NewGuid().ToString(),
                        card.Id.ToString()),
                ]));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        var proposal = (await createResponse.Content.ReadFromJsonAsync<ProposalDto>())!;

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/conflicts");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var conflicts = await response.Content.ReadFromJsonAsync<List<ConflictRowDto>>();

        conflicts.Should().NotBeNull();
        conflicts!.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Warn
            && row.Key == "unable-to-evaluate-operation"
            && row.Value.Contains("1", StringComparison.Ordinal));
        conflicts.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        conflicts.Should().NotContain(row =>
            row.Key == "status" && row.Value == "No conflicts detected");
        body.Should().NotContain(privatePayload);
    }
}
