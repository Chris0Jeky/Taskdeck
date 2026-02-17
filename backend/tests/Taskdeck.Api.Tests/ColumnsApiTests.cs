using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ColumnsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public ColumnsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ColumnsEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/boards/{boardId}/columns"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/columns",
                new CreateColumnDto(boardId, "Unauthorized", null, null)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PatchAsJsonAsync(
                $"/api/boards/{boardId}/columns/{columnId}",
                new UpdateColumnDto("Updated", null, null)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.DeleteAsync($"/api/boards/{boardId}/columns/{columnId}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/columns/reorder",
                new ReorderColumnsDto(new List<Guid> { columnId })));
    }

    [Fact]
    public async Task GetColumns_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        var board = await CreateBoardAsync();

        await ApiTestHarness.AuthenticateAsync(_client, "columns-other-user");
        _isAuthenticated = true;

        var response = await _client.GetAsync($"/api/boards/{board.Id}/columns");

        await ApiTestHarness.AssertForbiddenAsync(response);
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
        await EnsureAuthenticatedAsync();

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

    [Fact]
    public async Task UpdateColumn_ShouldReturnNotFound_WhenColumnBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardBColumn = await CreateColumnAsync(boardB.Id, "Other board column");

        var response = await _client.PatchAsJsonAsync(
            $"/api/boards/{boardA.Id}/columns/{boardBColumn.Id}",
            new UpdateColumnDto("Renamed", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task UpdateColumn_ShouldReturnBadRequest_WhenWipLimitIsInvalid()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "To Do");

        var response = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/columns/{column.Id}",
            new UpdateColumnDto(null, null, 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task DeleteColumn_ShouldReturnNotFound_WhenColumnBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardBColumn = await CreateColumnAsync(boardB.Id, "Other board column");

        var response = await _client.DeleteAsync($"/api/boards/{boardA.Id}/columns/{boardBColumn.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task ReorderColumns_ShouldReturnNotFound_WhenRequestContainsForeignColumn()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardAColumn = await CreateColumnAsync(boardA.Id, "A1");
        var boardBColumn = await CreateColumnAsync(boardB.Id, "B1");

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/columns/reorder",
            new ReorderColumnsDto(new List<Guid> { boardAColumn.Id, boardBColumn.Id }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "columns-board", "Column integration tests");
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

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "columns-suite");
        _isAuthenticated = true;
    }
}
