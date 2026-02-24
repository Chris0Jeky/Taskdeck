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

public class CardsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public CardsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CardsEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/boards/{boardId}/cards"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/boards/{boardId}/cards/{cardId}/provenance"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/cards",
                new CreateCardDto(boardId, columnId, "Unauthorized", null, null, null)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PatchAsJsonAsync(
                $"/api/boards/{boardId}/cards/{cardId}",
                new UpdateCardDto("Updated", null, null, null, null, null)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/cards/{cardId}/move",
                new MoveCardDto(columnId, 0)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.DeleteAsync($"/api/boards/{boardId}/cards/{cardId}"));
    }

    [Fact]
    public async Task GetCards_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        var board = await CreateBoardAsync();

        await ApiTestHarness.AuthenticateAsync(_client, "cards-other-user");
        _isAuthenticated = true;

        var response = await _client.GetAsync($"/api/boards/{board.Id}/cards");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task CreateCard_ShouldReturnBadRequest_WhenTargetColumnWipLimitExceeded()
    {
        var board = await CreateBoardAsync();
        var limitedColumn = await CreateColumnAsync(board.Id, "In Progress", wipLimit: 1);

        var firstCreateResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, limitedColumn.Id, "Card 1", null, null, null));

        firstCreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondCreateResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, limitedColumn.Id, "Card 2", null, null, null));

        secondCreateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await secondCreateResponse.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("WipLimitExceeded");
    }

    [Fact]
    public async Task MoveCard_ShouldMoveCardAcrossColumns()
    {
        var board = await CreateBoardAsync();
        var sourceColumn = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var targetColumn = await CreateColumnAsync(board.Id, "Done", wipLimit: null);

        var createCardResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, sourceColumn.Id, "Move me", null, null, null));
        createCardResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var card = await createCardResponse.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();

        var moveResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card!.Id}/move",
            new MoveCardDto(targetColumn.Id, 0));

        moveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var movedCard = await moveResponse.Content.ReadFromJsonAsync<CardDto>();
        movedCard.Should().NotBeNull();
        movedCard!.ColumnId.Should().Be(targetColumn.Id);
        movedCard.Position.Should().Be(0);
    }

    [Fact]
    public async Task MoveCard_ShouldReturnBadRequest_WhenTargetColumnWipLimitExceeded()
    {
        var board = await CreateBoardAsync();
        var sourceColumn = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var limitedTargetColumn = await CreateColumnAsync(board.Id, "In Progress", wipLimit: 1);

        await CreateCardAsync(board.Id, limitedTargetColumn.Id, "Existing target card");
        var cardToMove = await CreateCardAsync(board.Id, sourceColumn.Id, "Card to move");

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{cardToMove.Id}/move",
            new MoveCardDto(limitedTargetColumn.Id, 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("WipLimitExceeded");
    }

    [Fact]
    public async Task UpdateCard_ShouldReturnBadRequest_WhenTitleIsEmpty()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var card = await CreateCardAsync(board.Id, column.Id, "Valid title");

        var response = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto(string.Empty, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task UpdateCard_ShouldReturnConflict_WhenExpectedUpdatedAtIsStale()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var card = await CreateCardAsync(board.Id, column.Id, "Concurrency card");

        var firstUpdateResponse = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto("Updated by another session", null, null, null, null, null));
        firstUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var staleConflictResponse = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto(
                "Stale write",
                null,
                null,
                null,
                null,
                null,
                card.UpdatedAt));

        staleConflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var errorPayload = await staleConflictResponse.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("Conflict");
        errorPayload.GetProperty("message").GetString().Should().Contain("updated by another session");
    }

    [Fact]
    public async Task DeleteCard_ShouldReturnNotFound_WhenCardDoesNotExist()
    {
        var board = await CreateBoardAsync();

        var response = await _client.DeleteAsync($"/api/boards/{board.Id}/cards/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateCard_ShouldReturnNotFound_WhenColumnDoesNotExist()
    {
        var board = await CreateBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, Guid.NewGuid(), "Missing column", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task UpdateCard_ShouldReturnNotFound_WhenCardBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardBColumn = await CreateColumnAsync(boardB.Id, "To Do", wipLimit: null);
        var boardBCard = await CreateCardAsync(boardB.Id, boardBColumn.Id, "Card in board B");

        var response = await _client.PatchAsJsonAsync(
            $"/api/boards/{boardA.Id}/cards/{boardBCard.Id}",
            new UpdateCardDto("Updated", null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task MoveCard_ShouldReturnNotFound_WhenTargetColumnBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardAColumn = await CreateColumnAsync(boardA.Id, "To Do", wipLimit: null);
        var boardBColumn = await CreateColumnAsync(boardB.Id, "Other board", wipLimit: null);
        var boardACard = await CreateCardAsync(boardA.Id, boardAColumn.Id, "Card in board A");

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/cards/{boardACard.Id}/move",
            new MoveCardDto(boardBColumn.Id, 0));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task DeleteCard_ShouldReturnNotFound_WhenCardBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardBColumn = await CreateColumnAsync(boardB.Id, "To Do", wipLimit: null);
        var boardBCard = await CreateCardAsync(boardB.Id, boardBColumn.Id, "Card in board B");

        var response = await _client.DeleteAsync($"/api/boards/{boardA.Id}/cards/{boardBCard.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GetCardProvenance_ShouldReturnCaptureMetadata_ForCaptureCreatedCard()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "cards-provenance");
        _isAuthenticated = true;
        var board = await ApiTestHarness.CreateBoardAsync(_client, "cards-provenance-board");
        await CreateColumnAsync(board.Id, "Inbox", wipLimit: null);

        var createCaptureResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                """
                - [ ] Validate capture provenance endpoint
                """));
        createCaptureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var captureItem = await createCaptureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        captureItem.Should().NotBeNull();

        var triageResponse = await _client.PostAsync($"/api/capture/items/{captureItem!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triagedItem = await WaitForCaptureStatusAsync(_client, captureItem.Id, CaptureStatus.ProposalCreated);
        triagedItem.Provenance.Should().NotBeNull();
        triagedItem.Provenance!.ProposalId.Should().NotBeNull();

        var proposalId = triagedItem.Provenance.ProposalId!.Value;
        var approveResponse = await _client.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await ExecuteProposalAsync(_client, proposalId);

        var createdCard = await WaitForSingleCardAsync(_client, board.Id);

        var provenanceResponse = await _client.GetAsync($"/api/boards/{board.Id}/cards/{createdCard.Id}/provenance");
        provenanceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var provenance = await provenanceResponse.Content.ReadFromJsonAsync<CardCaptureProvenanceDto>();
        provenance.Should().NotBeNull();
        provenance!.CardId.Should().Be(createdCard.Id);
        provenance.CaptureItemId.Should().Be(captureItem.Id);
        provenance.ProposalId.Should().Be(proposalId);
        provenance.ProposalStatus.Should().Be(ProposalStatus.Applied);
        provenance.TriageRunId.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCardProvenance_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "cards-provenance-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "cards-provenance-outsider");

        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "cards-provenance-owner-board");
        var createColumnResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Inbox", null, null));
        createColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createCaptureResponse = await ownerClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                """
                - [ ] Verify cross-user provenance restriction
                """));
        createCaptureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var captureItem = await createCaptureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        captureItem.Should().NotBeNull();

        var triageResponse = await ownerClient.PostAsync($"/api/capture/items/{captureItem!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triagedItem = await WaitForCaptureStatusAsync(ownerClient, captureItem.Id, CaptureStatus.ProposalCreated);
        triagedItem.Provenance.Should().NotBeNull();
        triagedItem.Provenance!.ProposalId.Should().NotBeNull();
        var proposalId = triagedItem.Provenance.ProposalId!.Value;

        var approveResponse = await ownerClient.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await ExecuteProposalAsync(ownerClient, proposalId);

        var createdCard = await WaitForSingleCardAsync(ownerClient, board.Id);
        var response = await outsiderClient.GetAsync($"/api/boards/{board.Id}/cards/{createdCard.Id}/provenance");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "cards-board", "Card integration tests");
    }

    private async Task<ColumnDto> CreateColumnAsync(Guid boardId, string name, int? wipLimit)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns",
            new CreateColumnDto(boardId, name, null, wipLimit));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await response.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();
        return column!;
    }

    private async Task<CardDto> CreateCardAsync(Guid boardId, Guid columnId, string title)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, title, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();
        return card!;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "cards-suite");
        _isAuthenticated = true;
    }

    private async Task<CardDto> WaitForSingleCardAsync(HttpClient client, Guid boardId)
    {
        var cardList = await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var cardsResponse = await client.GetAsync($"/api/boards/{boardId}/cards");
                cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
                cards.Should().NotBeNull();
                return cards!;
            },
            cards => cards.Count == 1,
            $"single card to appear on board {boardId}",
            maxAttempts: 40,
            interval: TimeSpan.FromMilliseconds(250),
            diagnostics: cardList => cardList is null
                ? "cardList=null"
                : $"cardCount={cardList.Count}, cardIds=[{string.Join(",", cardList.Select(card => card.Id))}]");

        return cardList[0];
    }

    private async Task<CaptureItemDto> WaitForCaptureStatusAsync(Guid itemId, CaptureStatus expectedStatus)
    {
        return await WaitForCaptureStatusAsync(_client, itemId, expectedStatus);
    }

    private static async Task<CaptureItemDto> WaitForCaptureStatusAsync(
        HttpClient client,
        Guid itemId,
        CaptureStatus expectedStatus)
    {
        return await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/capture/items/{itemId}");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var item = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
                item.Should().NotBeNull();
                return item!;
            },
            item => item.Status == expectedStatus || (item.Status == CaptureStatus.Failed && expectedStatus != CaptureStatus.Failed),
            $"capture item {itemId} status to become {expectedStatus}",
            maxAttempts: 40,
            interval: TimeSpan.FromMilliseconds(250),
            diagnostics: item => item is null
                ? "item=null"
                : $"status={item.Status}, proposalId={item.Provenance?.ProposalId?.ToString() ?? "null"}, triageRunId={item.Provenance?.TriageRunId?.ToString() ?? "null"}");
    }

    private static async Task ExecuteProposalAsync(HttpClient client, Guid proposalId)
    {
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var executeResponse = await client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
