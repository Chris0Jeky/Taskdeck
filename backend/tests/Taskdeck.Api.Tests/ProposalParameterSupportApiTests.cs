using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ProposalParameterSupportApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("move", "dueDate")]
    [InlineData("delete", "labels")]
    [InlineData("move", "clearParent")]
    public async Task IgnoredFields_CannotPreviewOrApprove_AndLeaveTheCardUntouched(string action, string field)
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "ignored-card-field");
        var (boardId, card) = await CreateCardAsync(client);
        var parameters = new Dictionary<string, object?>
        {
            ["cardId"] = card.Id, ["columnId"] = card.ColumnId,
            ["expectedUpdatedAt"] = card.UpdatedAt,
            [field] = field == "dueDate" ? "2027-01-01" : field == "labels" ? new[] { "Ignored" } : false
        };
        var proposal = await CreateProposalAsync(client, user.UserId, boardId, card.Id, action, parameters);

        foreach (var route in new[] { "diff", "preview" })
        {
            using var read = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/{route}");
            await ApiTestHarness.AssertErrorContractAsync(read, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
            var error = await read.Content.ReadFromJsonAsync<JsonElement>();
            error.GetProperty("message").GetString().Should().Contain($"Parameter '{field}' is not supported");
        }
        using var approval = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        await ApiTestHarness.AssertErrorContractAsync(approval, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
        var after = await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}");
        after.Should().BeEquivalentTo(card);
        (await client.GetFromJsonAsync<ProposalDto>($"/api/automation/proposals/{proposal.Id}"))!
            .Status.Should().Be(ProposalStatus.PendingReview);
    }

    [Fact]
    public async Task SupportedUpdate_PreviewsAndAppliesNormalizedDueDateOnlyAfterExplicitApply()
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "supported-date-field");
        var (boardId, card) = await CreateCardAsync(client);
        var expected = new DateTimeOffset(2027, 1, 1, 7, 30, 0, TimeSpan.Zero);
        var proposal = await CreateProposalAsync(client, user.UserId, boardId, card.Id, "update",
            new { cardId = card.Id, dueDate = "2027-01-01T09:30:00+02:00", expectedUpdatedAt = card.UpdatedAt });
        using var preview = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        (await preview.Content.ReadAsStringAsync()).Should().Contain("set due date").And.Contain("2027-01-01");
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!.DueDate.Should().BeNull();
        using var approval = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approval.StatusCode.Should().Be(HttpStatusCode.OK, await approval.Content.ReadAsStringAsync());
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!.DueDate.Should().BeNull();
        using var apply = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        apply.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var applied = await client.SendAsync(apply);
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        var after = (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!;
        after.DueDate.Should().Be(expected);
        after.Id.Should().Be(card.Id);
        after.ColumnId.Should().Be(card.ColumnId);
        after.Title.Should().Be(card.Title);
    }

    private static async Task<(Guid BoardId, CardDto Card)> CreateCardAsync(HttpClient client)
    {
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "parameter-support");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        using var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, board.Columns.First().Id, "Unchanged until Apply", null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var card = await response.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();
        return (boardId, card!);
    }

    private static async Task<ProposalDto> CreateProposalAsync(HttpClient client, Guid userId, Guid boardId,
        Guid cardId, string action, object parameters)
    {
        // Generic draft admission intentionally remains shape-only; refusal belongs to
        // validated preview/approval/Apply, not to a new competing create API contract.
        using var response = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(
            ProposalSourceType.Manual, userId, "Card parameter contract", RiskLevel.Low,
            Guid.NewGuid().ToString(), boardId,
            Operations: [new CreateProposalOperationDto(0, action, "card", JsonSerializer.Serialize(parameters),
                Guid.NewGuid().ToString(), cardId.ToString())]));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var proposal = await response.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        return proposal!;
    }
}
