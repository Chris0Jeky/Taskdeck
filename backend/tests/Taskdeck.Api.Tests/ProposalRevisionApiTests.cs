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

    [Theory]
    [InlineData("2026-07-14T09:30:00")]
    [InlineData("07/14/2026")]
    public async Task RevisedDueDate_InvalidFormat_ShouldFailPreviewAndApply(string dueDate)
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-invalid-due");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-invalid-due-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);
        var revisedParameters = JsonSerializer.Serialize(new
        {
            title = "Invalid date card",
            boardId = board.Id,
            columnId = column.Id,
            dueDate
        });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayloadWithParameters(revisedParameters), reason = "invalid date" });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await client.SendAsync(executeRequest);
        await ApiTestHarness.AssertErrorContractAsync(executeResponse, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task RevisedLabels_MalformedArray_ShouldFailPreview()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-invalid-labels");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-invalid-labels-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);
        var revisedParameters = $$"""{"title":"Invalid labels","boardId":"{{board.Id}}","columnId":"{{column.Id}}","labels":["urgent",42]}""";

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayloadWithParameters(revisedParameters), reason = "invalid labels" });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task RevisedDueDate_SetAndClear_ShouldFailPreviewWithExactConflict()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-conflicting-due");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-conflicting-due-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);
        var revisedParameters = JsonSerializer.Serialize(new
        {
            title = "Conflicting date card",
            boardId = board.Id,
            columnId = column.Id,
            dueDate = "2026-07-14",
            clearDueDate = true
        });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload = BuildRevisionPayloadWithParameters(revisedParameters), reason = "conflicting due date" });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");
        var error = await diffResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("message").GetString().Should().Be(
            "Parameters 'dueDate' and 'clearDueDate' cannot both be specified");
    }

    [Fact]
    public async Task RevisedUpdate_WithOnlyFalseClearDueDate_ShouldFailPreviewAndApply()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-noop-update");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-noop-update-board");
        var card = await CreateCardAsync(client, board.Id, column.Id, "Unchanged card");
        var proposal = await CreateUpdateProposalAsync(client, user.UserId, board.Id, card.Id);
        var parameters = JsonSerializer.Serialize(new { cardId = card.Id, clearDueDate = false });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildSingleOperationRevisionPayload(
                    "update", "card", parameters, card.Id.ToString()),
                reason = "attempt a no-op update"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeResponse = await ExecuteProposalAsync(client, proposal.Id);
        await ApiTestHarness.AssertErrorContractAsync(executeResponse, HttpStatusCode.BadRequest, "ValidationError");

        var cards = await ReadCardsAsync(client, board.Id);
        cards.Should().ContainSingle(candidate => candidate.Id == card.Id && candidate.Title == "Unchanged card");
    }

    [Fact]
    public async Task RevisedLabelOperation_WithoutCardId_ShouldFailPreviewAndApply()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-label-missing-card");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-label-missing-card-board");
        var card = await CreateCardAsync(client, board.Id, column.Id, "Unchanged labels");
        var label = await CreateLabelAsync(client, board.Id, "urgent");
        var proposal = await CreateUpdateProposalAsync(client, user.UserId, board.Id, card.Id);
        var parameters = JsonSerializer.Serialize(new { labelId = label.Id });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildSingleOperationRevisionPayload("add-label", "card", parameters),
                reason = "omit the card identity"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeResponse = await ExecuteProposalAsync(client, proposal.Id);
        await ApiTestHarness.AssertErrorContractAsync(executeResponse, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task RevisedUpdate_WithEmptyLabelId_ShouldFailPreviewAndApply()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-empty-label-id");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-empty-label-id-board");
        var card = await CreateCardAsync(client, board.Id, column.Id, "Unchanged empty label card");
        var proposal = await CreateUpdateProposalAsync(client, user.UserId, board.Id, card.Id);
        var parameters = JsonSerializer.Serialize(new { cardId = card.Id, labelIds = new[] { Guid.Empty } });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildSingleOperationRevisionPayload(
                    "update", "card", parameters, card.Id.ToString()),
                reason = "attempt an empty label identity"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeResponse = await ExecuteProposalAsync(client, proposal.Id);
        await ApiTestHarness.AssertErrorContractAsync(executeResponse, HttpStatusCode.BadRequest, "ValidationError");

        var cards = await ReadCardsAsync(client, board.Id);
        cards.Should().ContainSingle(candidate => candidate.Id == card.Id && candidate.Labels.Count == 0);
    }

    [Theory]
    [InlineData("add--label")]
    [InlineData("add_-label")]
    [InlineData("remove__label")]
    [InlineData("add..label")]
    public async Task RevisedLabelOperation_WithUnregisteredAlias_ShouldFailPreviewAndApply(string actionType)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, $"rev-label-alias-{suffix}");
        var (board, column) = await CreateBoardWithColumnAsync(client, $"rev-label-alias-board-{suffix}");
        var card = await CreateCardAsync(client, board.Id, column.Id, "Unchanged alias card");
        var label = await CreateLabelAsync(client, board.Id, $"urgent-{suffix}");
        var proposal = await CreateUpdateProposalAsync(client, user.UserId, board.Id, card.Id);
        var parameters = JsonSerializer.Serialize(new { cardId = card.Id, labelId = label.Id });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildSingleOperationRevisionPayload(
                    actionType, "card", parameters, card.Id.ToString()),
                reason = "attempt an unregistered label alias"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeResponse = await ExecuteProposalAsync(client, proposal.Id);
        await ApiTestHarness.AssertErrorContractAsync(executeResponse, HttpStatusCode.BadRequest, "ValidationError");

        var cards = await ReadCardsAsync(client, board.Id);
        cards.Should().ContainSingle(candidate => candidate.Id == card.Id && candidate.Labels.Count == 0);
    }

    [Fact]
    public async Task RevisedCreate_WithoutRequiredTitle_ShouldFailPreviewAndApply()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rev-create-missing-title");
        var (board, column) = await CreateBoardWithColumnAsync(client, "rev-create-missing-title-board");
        var proposal = await CreateTestProposalAsync(client, user.UserId, board.Id, column.Id);
        var parameters = JsonSerializer.Serialize(new { boardId = board.Id, columnId = column.Id });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildSingleOperationRevisionPayload("create", "card", parameters),
                reason = "omit an apply-required create field"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeResponse = await ExecuteProposalAsync(client, proposal.Id);
        await ApiTestHarness.AssertErrorContractAsync(executeResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await ReadCardsAsync(client, board.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("update")]
    [InlineData("add-label")]
    public async Task RevisedLabelName_WithAmbiguousBoardMatches_ShouldFailPreviewAndApply(string actionType)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, $"rev-label-ambiguous-{suffix}");
        var (board, column) = await CreateBoardWithColumnAsync(client, $"rev-label-ambiguous-board-{suffix}");
        var card = await CreateCardAsync(client, board.Id, column.Id, "Unchanged ambiguous label card");
        var labelName = $"urgent-{suffix}";
        _ = await CreateLabelAsync(client, board.Id, labelName);
        _ = await CreateLabelAsync(client, board.Id, labelName.ToUpperInvariant());
        var proposal = await CreateUpdateProposalAsync(client, user.UserId, board.Id, card.Id);
        var parameters = actionType == "update"
            ? JsonSerializer.Serialize(new { cardId = card.Id, labels = new[] { labelName } })
            : JsonSerializer.Serialize(new { cardId = card.Id, labelName });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildSingleOperationRevisionPayload(
                    actionType, "card", parameters, card.Id.ToString()),
                reason = "attempt an ambiguous name-based label operation"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeResponse = await ExecuteProposalAsync(client, proposal.Id);
        await ApiTestHarness.AssertErrorContractAsync(executeResponse, HttpStatusCode.BadRequest, "ValidationError");

        var cards = await ReadCardsAsync(client, board.Id);
        cards.Should().ContainSingle(candidate => candidate.Id == card.Id && candidate.Labels.Count == 0);
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

    [Fact]
    public async Task RevisedUpdate_CannotRedirectAuthorizedTargetToAnotherUsersCard()
    {
        var ownerClient = _factory.CreateClient();
        var victimClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "rev-scope-update-owner");
        _ = await ApiTestHarness.AuthenticateAsync(victimClient, "rev-scope-update-victim");
        var (ownerBoard, ownerColumn) = await CreateBoardWithColumnAsync(ownerClient, "rev-scope-update-owner-board");
        var (victimBoard, victimColumn) = await CreateBoardWithColumnAsync(victimClient, "rev-scope-update-victim-board");
        var ownerCard = await CreateCardAsync(ownerClient, ownerBoard.Id, ownerColumn.Id, "Owner card");
        var victimCard = await CreateCardAsync(victimClient, victimBoard.Id, victimColumn.Id, "Victim card");
        var proposal = await CreateUpdateProposalAsync(ownerClient, owner.UserId, ownerBoard.Id, ownerCard.Id);

        var revisionResponse = await ownerClient.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildUpdateRevisionPayload(ownerCard.Id, victimCard.Id, "Redirected title"),
                reason = "attempt to redirect the parameter identity"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await ownerClient.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertErrorContractAsync(diffResponse, HttpStatusCode.BadRequest, "ValidationError");

        (await ownerClient.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await ownerClient.SendAsync(executeRequest);
        await ApiTestHarness.AssertErrorContractAsync(executeResponse, HttpStatusCode.BadRequest, "ValidationError");

        var victimCards = await ReadCardsAsync(victimClient, victimBoard.Id);
        victimCards.Should().ContainSingle(card => card.Id == victimCard.Id && card.Title == "Victim card");
    }

    [Fact]
    public async Task RevisedUpdate_CannotTargetAnotherUsersCardWithMatchingIdentities()
    {
        var ownerClient = _factory.CreateClient();
        var victimClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "rev-scope-matched-card-owner");
        _ = await ApiTestHarness.AuthenticateAsync(victimClient, "rev-scope-matched-card-victim");
        var (ownerBoard, ownerColumn) = await CreateBoardWithColumnAsync(ownerClient, "rev-scope-matched-card-owner-board");
        var (victimBoard, victimColumn) = await CreateBoardWithColumnAsync(victimClient, "rev-scope-matched-card-victim-board");
        var ownerCard = await CreateCardAsync(ownerClient, ownerBoard.Id, ownerColumn.Id, "Owner card");
        var victimCard = await CreateCardAsync(victimClient, victimBoard.Id, victimColumn.Id, "Victim card");
        var proposal = await CreateUpdateProposalAsync(ownerClient, owner.UserId, ownerBoard.Id, ownerCard.Id);

        var revisionResponse = await ownerClient.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildUpdateRevisionPayload(victimCard.Id, victimCard.Id, "Redirected title"),
                reason = "attempt to redirect both identities"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await ownerClient.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertForbiddenAsync(diffResponse);

        (await ownerClient.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeResponse = await ExecuteProposalAsync(ownerClient, proposal.Id);
        await ApiTestHarness.AssertForbiddenAsync(executeResponse);

        var victimCards = await ReadCardsAsync(victimClient, victimBoard.Id);
        victimCards.Should().ContainSingle(card => card.Id == victimCard.Id && card.Title == "Victim card");
    }

    [Fact]
    public async Task RevisedCreate_CannotRedirectBoardAndColumnToAnotherUsersBoard()
    {
        var ownerClient = _factory.CreateClient();
        var victimClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "rev-scope-create-owner");
        _ = await ApiTestHarness.AuthenticateAsync(victimClient, "rev-scope-create-victim");
        var (ownerBoard, ownerColumn) = await CreateBoardWithColumnAsync(ownerClient, "rev-scope-create-owner-board");
        var (victimBoard, victimColumn) = await CreateBoardWithColumnAsync(victimClient, "rev-scope-create-victim-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, ownerBoard.Id, ownerColumn.Id);

        var revisionResponse = await ownerClient.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildRevisionPayload("Cross-board card", victimBoard.Id, victimColumn.Id),
                reason = "attempt to redirect create scope"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await ownerClient.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertForbiddenAsync(diffResponse);

        (await ownerClient.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await ownerClient.SendAsync(executeRequest);
        await ApiTestHarness.AssertForbiddenAsync(executeResponse);

        var victimCards = await ReadCardsAsync(victimClient, victimBoard.Id);
        victimCards.Should().NotContain(card => card.Title == "Cross-board card");
    }

    [Fact]
    public async Task RevisedCreate_CannotUseAnotherUsersColumnWithinAuthorizedBoardParameters()
    {
        var ownerClient = _factory.CreateClient();
        var victimClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "rev-scope-column-owner");
        _ = await ApiTestHarness.AuthenticateAsync(victimClient, "rev-scope-column-victim");
        var (ownerBoard, ownerColumn) = await CreateBoardWithColumnAsync(ownerClient, "rev-scope-column-owner-board");
        var (victimBoard, victimColumn) = await CreateBoardWithColumnAsync(victimClient, "rev-scope-column-victim-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, ownerBoard.Id, ownerColumn.Id);

        var revisionResponse = await ownerClient.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new
            {
                revisedPayload = BuildRevisionPayload("Cross-board column card", ownerBoard.Id, victimColumn.Id),
                reason = "attempt to redirect only the column"
            });
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var diffResponse = await ownerClient.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertForbiddenAsync(diffResponse);

        (await ownerClient.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var executeResponse = await ExecuteProposalAsync(ownerClient, proposal.Id);
        await ApiTestHarness.AssertForbiddenAsync(executeResponse);

        var victimCards = await ReadCardsAsync(victimClient, victimBoard.Id);
        victimCards.Should().NotContain(card => card.Title == "Cross-board column card");
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

    private static async Task<CardDto> CreateCardAsync(
        HttpClient client,
        Guid boardId,
        Guid columnId,
        string title)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, title, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CardDto>())!;
    }

    private static async Task<LabelDto> CreateLabelAsync(HttpClient client, Guid boardId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/labels",
            new CreateLabelDto(boardId, name, "#FF0000"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<LabelDto>())!;
    }

    private static async Task<List<CardDto>> ReadCardsAsync(HttpClient client, Guid boardId)
    {
        var response = await client.GetAsync($"/api/boards/{boardId}/cards");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<CardDto>>())!;
    }

    private static async Task<HttpResponseMessage> ExecuteProposalAsync(HttpClient client, Guid proposalId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private static async Task<ProposalDto> CreateUpdateProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId,
        Guid cardId)
    {
        var parameters = JsonSerializer.Serialize(new { cardId, title = "Safe title" });
        var request = new CreateProposalDto(
            ProposalSourceType.Chat,
            userId,
            "Update one card",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            Operations: new List<CreateProposalOperationDto>
            {
                new(0, "update", "card", parameters, Guid.NewGuid().ToString(), cardId.ToString())
            });
        var response = await client.PostAsJsonAsync("/api/automation/proposals", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private static string BuildUpdateRevisionPayload(Guid targetId, Guid parameterCardId, string title)
    {
        return JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType = "update",
                    targetType = "card",
                    targetId = targetId.ToString(),
                    parameters = JsonSerializer.Serialize(new { cardId = parameterCardId, title }),
                    idempotencyKey = Guid.NewGuid().ToString(),
                    expectedVersion = (string?)null
                }
            }
        });
    }

    private static string BuildSingleOperationRevisionPayload(
        string actionType,
        string targetType,
        string parameters,
        string? targetId = null)
    {
        return JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType,
                    targetType,
                    targetId,
                    parameters,
                    idempotencyKey = Guid.NewGuid().ToString(),
                    expectedVersion = (string?)null
                }
            }
        });
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

    private static string BuildRevisionPayloadWithParameters(string parameters)
    {
        return JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 1,
                    actionType = "create",
                    targetType = "card",
                    targetId = (string?)null,
                    parameters,
                    idempotencyKey = Guid.NewGuid().ToString(),
                    expectedVersion = (string?)null
                }
            }
        });
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
