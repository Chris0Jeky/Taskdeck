using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Api.Tests;

public class CardsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

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

    private async Task<BoardDto> CreateBoardAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto($"Board-{Guid.NewGuid():N}", "Card integration tests"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await response.Content.ReadFromJsonAsync<BoardDto>();
        board.Should().NotBeNull();
        return board!;
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
}
