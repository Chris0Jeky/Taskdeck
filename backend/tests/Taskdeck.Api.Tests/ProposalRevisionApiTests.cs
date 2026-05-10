using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ProposalRevisionApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProposalRevisionApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateRevision_HappyPath_ShouldReturnCreated()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-create-happy");
        var board = await ApiTestHarness.CreateBoardAsync(client, "rev-create-happy-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{\"title\":\"Edited Card\"}", reason = "Adjusted title" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var revision = await response.Content.ReadFromJsonAsync<ProposalRevisionDto>();
        revision.Should().NotBeNull();
        revision!.ProposalId.Should().Be(proposal.Id);
        revision.RevisionNumber.Should().Be(1);
        revision.EditorUserId.Should().Be(user.UserId);
        revision.RevisedPayload.Should().Be("{\"title\":\"Edited Card\"}");
        revision.Reason.Should().Be("Adjusted title");
    }

    [Fact]
    public async Task CreateRevision_OnNonExistentProposal_ShouldReturn404()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "rev-create-notfound");

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{Guid.NewGuid()}/revisions",
            new { revisedPayload = "{}", reason = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateRevision_OnApprovedProposal_ShouldReturnConflict()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-create-conflict");
        var board = await ApiTestHarness.CreateBoardAsync(client, "rev-create-conflict-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id);

        await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{}", reason = "too late" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateRevision_WithInvalidRevisedPayloadJson_ShouldReturnValidationError()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-create-invalid-json");
        var board = await ApiTestHarness.CreateBoardAsync(client, "rev-create-invalid-json-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{\"operations\":[", reason = "malformed edit" });

        await ApiTestHarness.AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "ValidationError");
    }

    [Fact]
    public async Task GetRevisions_Empty_ShouldReturnEmptyList()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-list-empty");
        var board = await ApiTestHarness.CreateBoardAsync(client, "rev-list-empty-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id);

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/revisions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var revisions = await response.Content.ReadFromJsonAsync<List<ProposalRevisionDto>>();
        revisions.Should().NotBeNull();
        revisions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRevisions_Populated_ShouldReturnAll()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-list-pop");
        var board = await ApiTestHarness.CreateBoardAsync(client, "rev-list-pop-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id);

        await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{\"v\":1}", reason = "first edit" });
        await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{\"v\":2}", reason = "second edit" });

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/revisions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var revisions = await response.Content.ReadFromJsonAsync<List<ProposalRevisionDto>>();
        revisions.Should().NotBeNull();
        revisions.Should().HaveCount(2);
        revisions![0].RevisionNumber.Should().BeLessThan(revisions[1].RevisionNumber);
    }

    [Fact]
    public async Task GetLatestRevision_WhenNoneExist_ShouldReturn404()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-latest-none");
        var board = await ApiTestHarness.CreateBoardAsync(client, "rev-latest-none-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id);

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/revisions/latest");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLatestRevision_WhenRevisionsExist_ShouldReturnLatest()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-latest-exists");
        var board = await ApiTestHarness.CreateBoardAsync(client, "rev-latest-exists-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id);

        await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{\"v\":1}", reason = "first" });
        await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{\"v\":2}", reason = "second" });

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/revisions/latest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var revision = await response.Content.ReadFromJsonAsync<ProposalRevisionDto>();
        revision.Should().NotBeNull();
        revision!.RevisedPayload.Should().Be("{\"v\":2}");
        revision.RevisionNumber.Should().Be(2);
    }

    [Fact]
    public async Task CreateRevision_ShouldReturnForbidden_WhenCallerCannotWriteProposalBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "rev-authz-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "rev-authz-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "rev-authz-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id);

        var response = await outsiderClient.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{}", reason = "unauthorized" });

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private static async Task<ProposalDto> CreateTestProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId)
    {
        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: userId,
            Summary: $"Revision test proposal {Guid.NewGuid()}",
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardId,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 1,
                    ActionType: "CreateCard",
                    TargetType: "Card",
                    Parameters: "{\"title\":\"Test Card\"}",
                    IdempotencyKey: Guid.NewGuid().ToString())
            });

        var response = await client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }
}
