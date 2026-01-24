using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Api.Tests;

public class BoardsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BoardsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateBoard_ThenGetBoardDetail_ShouldReturnCreatedBoard()
    {
        var createRequest = new CreateBoardDto(
            Name: $"Board-{Guid.NewGuid():N}",
            Description: "Integration test board");

        var createResponse = await _client.PostAsJsonAsync("/api/boards", createRequest);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdBoard = await createResponse.Content.ReadFromJsonAsync<BoardDto>();
        createdBoard.Should().NotBeNull();
        createdBoard!.Name.Should().Be(createRequest.Name);
        createdBoard.Description.Should().Be(createRequest.Description);
        createdBoard.IsArchived.Should().BeFalse();

        var detailResponse = await _client.GetAsync($"/api/boards/{createdBoard.Id}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await detailResponse.Content.ReadFromJsonAsync<BoardDetailDto>();
        detail.Should().NotBeNull();
        detail!.Id.Should().Be(createdBoard.Id);
        detail.Columns.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteBoard_ShouldArchiveAndHideFromDefaultList()
    {
        var createResponse = await _client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto($"Archive-{Guid.NewGuid():N}", "Archive flow"));
        createResponse.EnsureSuccessStatusCode();

        var createdBoard = await createResponse.Content.ReadFromJsonAsync<BoardDto>();
        createdBoard.Should().NotBeNull();

        var deleteResponse = await _client.DeleteAsync($"/api/boards/{createdBoard!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var activeListResponse = await _client.GetAsync("/api/boards");
        activeListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeBoards = await activeListResponse.Content.ReadFromJsonAsync<List<BoardDto>>();
        activeBoards.Should().NotBeNull();
        activeBoards.Should().NotContain(b => b.Id == createdBoard.Id);

        var fullListResponse = await _client.GetAsync("/api/boards?includeArchived=true");
        fullListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allBoards = await fullListResponse.Content.ReadFromJsonAsync<List<BoardDto>>();
        allBoards.Should().NotBeNull();
        allBoards.Should().ContainSingle(b => b.Id == createdBoard.Id && b.IsArchived);
    }
}
