using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class BoardsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public BoardsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateBoard_ThenGetBoardDetail_ShouldReturnCreatedBoard()
    {
        await EnsureAuthenticatedAsync();

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
    public async Task BoardsEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var anonymousClient = _factory.CreateClient();

        var listResponse = await anonymousClient.GetAsync("/api/boards");
        await ApiTestHarness.AssertUnauthorizedAsync(listResponse);

        var getResponse = await anonymousClient.GetAsync($"/api/boards/{Guid.NewGuid()}");
        await ApiTestHarness.AssertUnauthorizedAsync(getResponse);

        var createResponse = await anonymousClient.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto($"Board-{Guid.NewGuid():N}", "Unauthorized"));
        await ApiTestHarness.AssertUnauthorizedAsync(createResponse);

        var updateResponse = await anonymousClient.PutAsJsonAsync(
            $"/api/boards/{Guid.NewGuid()}",
            new UpdateBoardDto("Renamed", null, null));
        await ApiTestHarness.AssertUnauthorizedAsync(updateResponse);

        var deleteResponse = await anonymousClient.DeleteAsync($"/api/boards/{Guid.NewGuid()}");
        await ApiTestHarness.AssertUnauthorizedAsync(deleteResponse);
    }

    [Fact]
    public async Task GetBoard_ShouldReturnForbidden_ForDifferentUser()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "board-owner");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "owner-board");

        using var otherClient = _factory.CreateClient();
        var other = await ApiTestHarness.AuthenticateAsync(otherClient, "board-other");

        await ApiTestHarness.AssertCrossUserIsolationAsync(
            () => otherClient.GetAsync($"/api/boards/{board.Id}"));

        other.UserId.Should().NotBe(owner.UserId);
    }

    [Fact]
    public async Task DeleteBoard_ShouldReturnForbidden_ForDifferentUser()
    {
        using var ownerClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "board-delete-owner");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "board-delete");

        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "board-delete-other");

        await ApiTestHarness.AssertCrossUserIsolationAsync(
            () => otherClient.DeleteAsync($"/api/boards/{board.Id}"));
    }

    [Fact]
    public async Task ListBoards_ShouldReturnOnlyBoardsVisibleToCaller()
    {
        using var ownerClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "list-owner");
        var ownerBoard = await ApiTestHarness.CreateBoardAsync(ownerClient, "list-owner-board");

        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "list-other");
        var otherBoard = await ApiTestHarness.CreateBoardAsync(otherClient, "list-other-board");

        var ownerListResponse = await ownerClient.GetAsync("/api/boards");
        ownerListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerBoards = await ownerListResponse.Content.ReadFromJsonAsync<List<BoardDto>>();
        ownerBoards.Should().NotBeNull();
        ownerBoards.Should().ContainSingle(b => b.Id == ownerBoard.Id);
        ownerBoards.Should().NotContain(b => b.Id == otherBoard.Id);
    }

    [Fact]
    public async Task CreateBoard_ShouldReturnBadRequest_WhenNameIsEmpty()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto(string.Empty, "Invalid board"));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task UpdateBoard_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PutAsJsonAsync(
            $"/api/boards/{Guid.NewGuid()}",
            new UpdateBoardDto("Renamed", null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task DeleteBoard_ShouldArchiveAndHideFromDefaultList()
    {
        await EnsureAuthenticatedAsync();

        var createdBoard = await ApiTestHarness.CreateBoardAsync(_client, "archive-flow");

        var deleteResponse = await _client.DeleteAsync($"/api/boards/{createdBoard.Id}");
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

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "boards-suite");
        _isAuthenticated = true;
    }
}
