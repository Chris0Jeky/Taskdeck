using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LabelsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LabelsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateUpdateDeleteLabel_ShouldCompleteLifecycle()
    {
        var board = await CreateBoardAsync();

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "Priority", "#3366FF"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdLabel = await createResponse.Content.ReadFromJsonAsync<LabelDto>();
        createdLabel.Should().NotBeNull();
        createdLabel!.Name.Should().Be("Priority");
        createdLabel.ColorHex.Should().Be("#3366FF");

        var listAfterCreateResponse = await _client.GetAsync($"/api/boards/{board.Id}/labels");
        listAfterCreateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var labelsAfterCreate = await listAfterCreateResponse.Content.ReadFromJsonAsync<List<LabelDto>>();
        labelsAfterCreate.Should().NotBeNull();
        labelsAfterCreate.Should().ContainSingle(l => l.Id == createdLabel.Id);

        var updateResponse = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/labels/{createdLabel.Id}",
            new UpdateLabelDto("Urgent", "#FF0000"));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedLabel = await updateResponse.Content.ReadFromJsonAsync<LabelDto>();
        updatedLabel.Should().NotBeNull();
        updatedLabel!.Name.Should().Be("Urgent");
        updatedLabel.ColorHex.Should().Be("#FF0000");

        var deleteResponse = await _client.DeleteAsync($"/api/boards/{board.Id}/labels/{createdLabel.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfterDeleteResponse = await _client.GetAsync($"/api/boards/{board.Id}/labels");
        listAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var labelsAfterDelete = await listAfterDeleteResponse.Content.ReadFromJsonAsync<List<LabelDto>>();
        labelsAfterDelete.Should().NotBeNull();
        labelsAfterDelete.Should().NotContain(l => l.Id == createdLabel.Id);
    }

    [Fact]
    public async Task CreateLabel_ShouldReturnBadRequest_WhenColorIsInvalid()
    {
        var board = await CreateBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "Priority", "blue"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task DeleteLabel_ShouldReturnNotFound_WhenLabelDoesNotExist()
    {
        var board = await CreateBoardAsync();

        var response = await _client.DeleteAsync($"/api/boards/{board.Id}/labels/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto($"Board-{Guid.NewGuid():N}", "Label integration tests"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await response.Content.ReadFromJsonAsync<BoardDto>();
        board.Should().NotBeNull();
        return board!;
    }
}
