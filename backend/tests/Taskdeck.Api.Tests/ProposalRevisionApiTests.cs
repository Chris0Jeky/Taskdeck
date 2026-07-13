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
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-create-happy-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("Edited Card", board.Id, column.Id), reason = "Adjusted title" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var revision = await response.Content.ReadFromJsonAsync<ProposalRevisionDto>();
        revision.Should().NotBeNull();
        revision!.ProposalId.Should().Be(proposal.Id);
        revision.RevisionNumber.Should().Be(1);
        revision.EditorUserId.Should().Be(user.UserId);
        revision.RevisedPayload.Should().Contain("Edited Card");
        revision.Reason.Should().Be("Adjusted title");
    }

    [Fact]
    public async Task CreateRevision_OnNonExistentProposal_ShouldReturn404()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "rev-create-notfound");

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{Guid.NewGuid()}/revisions",
            new { revisedPayload = BuildRevisionPayload("Edited Card", Guid.NewGuid(), Guid.NewGuid()), reason = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateRevision_OnApprovedProposal_ShouldReturnConflict()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-create-conflict");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-create-conflict-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);

        await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("Edited Card", board.Id, column.Id), reason = "too late" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateRevision_WithInvalidRevisedPayloadJson_ShouldReturnValidationError()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-create-invalid-json");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-create-invalid-json-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{\"operations\":[", reason = "malformed edit" });

        await ApiTestHarness.AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "ValidationError");
    }

    [Fact]
    public async Task CreateRevision_WithNonArrayOperations_ShouldReturnValidationError()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-create-invalid-shape");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-create-invalid-shape-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = "{\"operations\":\"not an array\"}", reason = "broken snapshot" });

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
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-list-empty-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);

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
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-list-pop-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);

        await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("First Edit", board.Id, column.Id), reason = "first edit" });
        await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("Second Edit", board.Id, column.Id), reason = "second edit" });

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
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-latest-none-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/revisions/latest");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLatestRevision_WhenRevisionsExist_ShouldReturnLatest()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-latest-exists");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-latest-exists-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);

        await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("First Edit", board.Id, column.Id), reason = "first" });
        await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("Second Edit", board.Id, column.Id), reason = "second" });

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/revisions/latest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var revision = await response.Content.ReadFromJsonAsync<ProposalRevisionDto>();
        revision.Should().NotBeNull();
        revision!.RevisedPayload.Should().Contain("Second Edit");
        revision.RevisionNumber.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteProposal_WithLatestRevision_ShouldApplyRevisedOperations()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-execute-latest");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-execute-latest-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id, "Original Card");

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("Edited Card", board.Id, column.Id), reason = "Use edited title" });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var cardsResponse = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();

        cards.Should().NotBeNull();
        cards!.Should().ContainSingle(card => card.Title == "Edited Card");
        cards.Should().NotContain(card => card.Title == "Original Card");
    }

    [Fact]
    public async Task GetProposalDiff_AfterSavingRevision_ShouldReflectRevisedOperations()
    {
        // #1235, exit criterion (b): the approval-gate diff must equal what Apply
        // executes. Paired with ExecuteProposal_WithLatestRevision_ShouldApplyRevisedOperations
        // (which proves Apply runs the revised "Edited Card"), this proves preview == apply.
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-diff-aware");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-diff-aware-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id, "Original Card");

        // Baseline: with no revision, the diff describes the original proposal.
        var beforeResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        beforeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeDiff = (await beforeResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("diff").GetString();
        beforeDiff.Should().Contain("Original Card");

        // Edit-before-approve: save a revision changing the card title.
        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("Edited Card", board.Id, column.Id), reason = "Use edited title" });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // The diff must now reflect the revised operations, not the original.
        var afterResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDiff = (await afterResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("diff").GetString();
        afterDiff.Should().Contain("Edited Card");
        afterDiff.Should().NotContain("Original Card");
    }

    [Fact]
    public async Task RevisedDueDate_PreviewShouldEqualAppliedCardDueDate()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-due-date");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-due-date-board");
        var originalDueDate = new DateTimeOffset(2026, 7, 14, 9, 30, 0, TimeSpan.FromHours(2));
        var revisedDueDate = new DateTimeOffset(2026, 7, 20, 16, 0, 0, TimeSpan.FromHours(-4));
        var proposal = await CreateTestProposalAsync(
            client, user.UserId, board.Id, column.Id, "Dated Card", originalDueDate);

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildRevisionPayload("Dated Card", board.Id, column.Id, revisedDueDate),
                reason = "Use revised due date"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        diffResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var diff = (await diffResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("diff").GetString();
        diff.Should().Contain($"set due date to {revisedDueDate.ToUniversalTime():O}");
        diff.Should().NotContain(originalDueDate.ToUniversalTime().ToString("O"));

        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var cardsResponse = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().ContainSingle(card =>
            card.Title == "Dated Card" && card.DueDate == revisedDueDate.ToUniversalTime());
    }

    [Fact]
    public async Task CreateRevision_ShouldReturnForbidden_WhenCallerCannotWriteProposalBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "rev-authz-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "rev-authz-outsider");
        var (board, column) = await CreateBoardWithColumnAsync(ownerClient, "rev-authz-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, column.Id);

        var response = await outsiderClient.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayload("Edited Card", board.Id, column.Id), reason = "unauthorized" });

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private static async Task<ProposalDto> CreateTestProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId,
        Guid columnId,
        string title = "Test Card",
        DateTimeOffset? dueDate = null)
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
                    ActionType: "create",
                    TargetType: "card",
                    Parameters: BuildCreateCardParameters(title, boardId, columnId, dueDate),
                    IdempotencyKey: Guid.NewGuid().ToString())
            });

        var response = await client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private static async Task<(BoardDto Board, ColumnDto Column)> CreateBoardWithColumnAsync(
        HttpClient client,
        string stem)
    {
        var board = await ApiTestHarness.CreateBoardAsync(client, stem);
        var columnResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "To Do", 0, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var column = await columnResponse.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();
        return (board, column!);
    }

    private static string BuildRevisionPayload(
        string title,
        Guid boardId,
        Guid columnId,
        DateTimeOffset? dueDate = null)
    {
        var payload = new
        {
            operations = new[]
            {
                new
                {
                    sequence = 1,
                    actionType = "create",
                    targetType = "card",
                    targetId = (string?)null,
                    parameters = BuildCreateCardParameters(title, boardId, columnId, dueDate),
                    idempotencyKey = Guid.NewGuid().ToString(),
                    expectedVersion = (string?)null
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildCreateCardParameters(
        string title,
        Guid boardId,
        Guid columnId,
        DateTimeOffset? dueDate = null)
    {
        var parameters = new Dictionary<string, object>
        {
            ["title"] = title,
            ["boardId"] = boardId,
            ["columnId"] = columnId
        };
        if (dueDate.HasValue)
            parameters["dueDate"] = dueDate.Value.ToString("O");

        return JsonSerializer.Serialize(parameters);
    }
}
