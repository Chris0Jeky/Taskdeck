using System.Net;
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

public sealed class ProposalConflictShapeApiTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("replace-assignments", "card")]
    [InlineData("add-relation", "card")]
    [InlineData("remove-relation", "card")]
    [InlineData("add-label", "card")]
    [InlineData("update", "card")]
    [InlineData("reorder", "column")]
    public async Task GetConflicts_HistoricalSupportedMalformedShape_HasNoOkOrPrivatePayload(string action, string target)
    {
        const string privatePayload = "operation-private-payload-not-for-conflict-response";
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "conflicts-full-shape");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "conflicts-full-shape");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var columnId = board.Columns.Single().Id;
        var cardResponse = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, "Historical target", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created, await cardResponse.Content.ReadAsStringAsync());
        var card = (await cardResponse.Content.ReadFromJsonAsync<CardDto>())!;
        var createResponse = await client.PostAsJsonAsync("/api/automation/proposals",
            new CreateProposalDto(ProposalSourceType.Manual, user.UserId, "Historical shape", RiskLevel.Low,
                Guid.NewGuid().ToString(), boardId, Operations:
                [
                    new CreateProposalOperationDto(0, "update", "card",
                        JsonSerializer.Serialize(new { boardId, cardId = card.Id, title = "Proposed title" }),
                        Guid.NewGuid().ToString(), card.Id.ToString()),
                ]));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        var proposal = (await createResponse.Content.ReadFromJsonAsync<ProposalDto>())!;

        // Simulate a legacy persisted shape. Admission of new malformed requests is
        // not the contract under test: old rows must remain readable and fail closed.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var operation = await db.AutomationProposalOperations.SingleAsync(op => op.ProposalId == proposal.Id);
            var parameters = new Dictionary<string, object> { ["privateNote"] = privatePayload };
            if (target == "column") parameters["columnId"] = columnId;
            else if (action != "update") parameters["cardId"] = card.Id;
            else parameters["title"] = "Replacement title"; // Missing required cardId.
            db.Entry(operation).Property(op => op.ActionType).CurrentValue = action;
            db.Entry(operation).Property(op => op.TargetType).CurrentValue = target;
            db.Entry(operation).Property(op => op.TargetId).CurrentValue = (target == "column" ? columnId : card.Id).ToString();
            db.Entry(operation).Property(op => op.Parameters).CurrentValue = JsonSerializer.Serialize(parameters);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/conflicts");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var rows = (await response.Content.ReadFromJsonAsync<List<ConflictRowDto>>())!;
        rows.Should().ContainSingle(row => row.Key == "unable-to-evaluate-operation" && row.Tone == ConflictTone.Warn);
        rows.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        body.Should().NotContain(privatePayload);
        body.Should().NotContain("privateNote");
    }
}
