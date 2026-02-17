using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LabelsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public LabelsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LabelsEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var boardId = Guid.NewGuid();
        var labelId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/boards/{boardId}/labels"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/labels",
                new CreateLabelDto(boardId, "Unauthorized", "#123456")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PatchAsJsonAsync(
                $"/api/boards/{boardId}/labels/{labelId}",
                new UpdateLabelDto("Updated", "#654321")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.DeleteAsync($"/api/boards/{boardId}/labels/{labelId}"));
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

    [Fact]
    public async Task CreateLabel_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        await EnsureAuthenticatedAsync();

        var missingBoardId = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{missingBoardId}/labels",
            new CreateLabelDto(missingBoardId, "Priority", "#3366FF"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task UpdateLabel_ShouldReturnBadRequest_WhenColorIsInvalid()
    {
        var board = await CreateBoardAsync();
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "Priority", "#3366FF"));
        createResponse.EnsureSuccessStatusCode();
        var label = await createResponse.Content.ReadFromJsonAsync<LabelDto>();
        label.Should().NotBeNull();

        var response = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/labels/{label!.Id}",
            new UpdateLabelDto("Priority", "invalid"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task UpdateLabel_ShouldReturnNotFound_WhenLabelBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var createOnBResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{boardB.Id}/labels",
            new CreateLabelDto(boardB.Id, "Board B Label", "#10B981"));
        createOnBResponse.EnsureSuccessStatusCode();
        var boardBLabel = await createOnBResponse.Content.ReadFromJsonAsync<LabelDto>();
        boardBLabel.Should().NotBeNull();

        var response = await _client.PatchAsJsonAsync(
            $"/api/boards/{boardA.Id}/labels/{boardBLabel!.Id}",
            new UpdateLabelDto("Updated", "#EF4444"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task DeleteLabel_ShouldReturnNotFound_WhenLabelBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var createOnBResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{boardB.Id}/labels",
            new CreateLabelDto(boardB.Id, "Board B Label", "#10B981"));
        createOnBResponse.EnsureSuccessStatusCode();
        var boardBLabel = await createOnBResponse.Content.ReadFromJsonAsync<LabelDto>();
        boardBLabel.Should().NotBeNull();

        var response = await _client.DeleteAsync($"/api/boards/{boardA.Id}/labels/{boardBLabel!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "labels-board", "Label integration tests");
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "labels-suite");
        _isAuthenticated = true;
    }
}
