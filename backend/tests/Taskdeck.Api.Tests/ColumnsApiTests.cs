using System.Net;
using System.Net.Http.Json;
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
}
