using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public CardsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
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
}
