using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ColumnsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ColumnsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReorderColumns_ShouldReturnColumnsInRequestedOrder()
    {
        var board = await CreateBoardAsync();
        var first = await CreateColumnAsync(board.Id, "One");
        var second = await CreateColumnAsync(board.Id, "Two");
        var third = await CreateColumnAsync(board.Id, "Three");

        var reorderRequest = new ReorderColumnsDto(new List<Guid>
        {
            second.Id,
            third.Id,
            first.Id
        });

        var reorderResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/reorder",
            reorderRequest);

        reorderResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reorderedColumns = await reorderResponse.Content.ReadFromJsonAsync<List<ColumnDto>>();
        reorderedColumns.Should().NotBeNull();
        reorderedColumns!.Select(c => c.Id).Should().ContainInOrder(second.Id, third.Id, first.Id);
    }

    [Fact]
    public async Task CreateColumn_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{Guid.NewGuid()}/columns",
            new CreateColumnDto(Guid.Empty, "Missing board", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task DeleteColumn_ShouldReturnConflict_WhenColumnContainsCards()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "In Progress");
        await CreateCardAsync(board.Id, column.Id, "Existing card");

        var response = await _client.DeleteAsync($"/api/boards/{board.Id}/columns/{column.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("Conflict");
    }

    [Fact]
    public async Task ReorderColumns_ShouldReturnBadRequest_WhenRequestDoesNotIncludeAllColumns()
    {
        var board = await CreateBoardAsync();
        var first = await CreateColumnAsync(board.Id, "One");
        await CreateColumnAsync(board.Id, "Two");

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns/reorder",
            new ReorderColumnsDto(new List<Guid> { first.Id }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto($"Board-{Guid.NewGuid():N}", "Column integration tests"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await response.Content.ReadFromJsonAsync<BoardDto>();
        board.Should().NotBeNull();
        return board!;
    }

    private async Task<ColumnDto> CreateColumnAsync(Guid boardId, string name)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns",
            new CreateColumnDto(boardId, name, null, null));

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
}
