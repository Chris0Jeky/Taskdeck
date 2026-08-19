using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
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

        var response = await otherClient.GetAsync($"/api/boards/{board.Id}");
        await ApiTestHarness.AssertForbiddenAsync(response);

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

        var response = await otherClient.DeleteAsync($"/api/boards/{board.Id}");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task UpdateBoard_ShouldReturnForbidden_ForDifferentUser()
    {
        using var ownerClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "board-update-owner");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "board-update");

        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "board-update-other");

        var response = await otherClient.PutAsJsonAsync(
            $"/api/boards/{board.Id}",
            new UpdateBoardDto("renamed-by-outsider", null, null));

        await ApiTestHarness.AssertForbiddenAsync(response);
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

        var ownerBoards = await ApiTestHarness.ListBoardsAsync(ownerClient);
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

        var activeBoards = await ApiTestHarness.ListBoardsAsync(_client);
        activeBoards.Should().NotContain(b => b.Id == createdBoard.Id);

        var allBoards = await ApiTestHarness.ListBoardsAsync(_client, includeArchived: true);
        allBoards.Should().ContainSingle(b => b.Id == createdBoard.Id && b.IsArchived);
    }

    [Fact]
    public async Task ArchivedBoard_ShouldBeRestorable_ViaUpdateEndpoint()
    {
        await EnsureAuthenticatedAsync();

        var createdBoard = await ApiTestHarness.CreateBoardAsync(_client, "restore-flow");

        var archiveResponse = await _client.DeleteAsync($"/api/boards/{createdBoard.Id}");
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restoreResponse = await _client.PutAsJsonAsync(
            $"/api/boards/{createdBoard.Id}",
            new UpdateBoardDto(null, null, false));

        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoredBoard = await restoreResponse.Content.ReadFromJsonAsync<BoardDto>();
        restoredBoard.Should().NotBeNull();
        restoredBoard!.Id.Should().Be(createdBoard.Id);
        restoredBoard.IsArchived.Should().BeFalse();

        var activeBoards = await ApiTestHarness.ListBoardsAsync(_client);
        activeBoards.Should().ContainSingle(b => b.Id == createdBoard.Id && !b.IsArchived);
    }

    [Fact]
    public async Task UpdateBoardArchiveState_ShouldSupportArchiveAndRestoreLifecycleTransitions()
    {
        await EnsureAuthenticatedAsync();

        var createdBoard = await ApiTestHarness.CreateBoardAsync(_client, "lifecycle-transition");

        var archiveResponse = await _client.PutAsJsonAsync(
            $"/api/boards/{createdBoard.Id}",
            new UpdateBoardDto(null, null, true));

        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeBoards = await ApiTestHarness.ListBoardsAsync(_client);
        activeBoards.Should().NotContain(board => board.Id == createdBoard.Id);

        var allBoardsAfterArchive = await ApiTestHarness.ListBoardsAsync(_client, includeArchived: true);
        allBoardsAfterArchive.Should().ContainSingle(board => board.Id == createdBoard.Id && board.IsArchived);

        var restoreResponse = await _client.PutAsJsonAsync(
            $"/api/boards/{createdBoard.Id}",
            new UpdateBoardDto(null, null, false));

        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeBoardsAfterRestore = await ApiTestHarness.ListBoardsAsync(_client);
        activeBoardsAfterRestore.Should().ContainSingle(board => board.Id == createdBoard.Id && !board.IsArchived);
    }

    // ---------------------------------------------------------------------
    // BoardDto.CanWrite — the server-computed write signal the Paper board
    // picker uses to disable read-only boards (#1836 item 1).
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetBoards_ShouldStampCanWriteTrue_ForTheBoardOwner()
    {
        // Owners have no BoardAccess row at all, so this can only come from the
        // ownership short-circuit — the exact case permissionsStore.canEdit gets wrong.
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "canwrite-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "canwrite-owned");

        var boards = await ApiTestHarness.ListBoardsAsync(client);

        boards.Single(b => b.Id == board.Id).CanWrite.Should().BeTrue();
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Editor)]
    public async Task GetBoards_ShouldStampCanWriteTrue_ForWriteCapableMembers(UserRole role)
    {
        using var ownerClient = _factory.CreateClient();
        using var memberClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "canwrite-grantor");
        var member = await ApiTestHarness.AuthenticateAsync(memberClient, "canwrite-member");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "canwrite-shared");

        var grant = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, member.UserId, role));
        grant.StatusCode.Should().Be(HttpStatusCode.OK);

        var boards = await ApiTestHarness.ListBoardsAsync(memberClient);

        boards.Single(b => b.Id == board.Id).CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task GetBoards_ShouldStampCanWriteFalse_ForAViewerMember()
    {
        // The picker case: a Viewer still SEES the board (read access) but must not be
        // able to triage into it — the board is rendered disabled, not filtered away.
        using var ownerClient = _factory.CreateClient();
        using var viewerClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "canwrite-viewer-grantor");
        var viewer = await ApiTestHarness.AuthenticateAsync(viewerClient, "canwrite-viewer");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "canwrite-viewonly");

        var grant = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, viewer.UserId, UserRole.Viewer));
        grant.StatusCode.Should().Be(HttpStatusCode.OK);

        var boards = await ApiTestHarness.ListBoardsAsync(viewerClient);

        var listed = boards.Single(b => b.Id == board.Id);
        listed.CanWrite.Should().BeFalse();
    }

    [Fact]
    public async Task GetBoards_ShouldNotListABoardAtAll_ForANonMember()
    {
        // "canWrite: false for a non-member" is unreachable through this endpoint by
        // construction: the read gate removes the board before it can be stamped. This
        // pins that, so a future change that widens the list can't quietly leak a board
        // with a plausible-looking false.
        using var ownerClient = _factory.CreateClient();
        using var strangerClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "canwrite-stranger-owner");
        await ApiTestHarness.AuthenticateAsync(strangerClient, "canwrite-stranger");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "canwrite-private");

        var boards = await ApiTestHarness.ListBoardsAsync(strangerClient);

        boards.Should().NotContain(b => b.Id == board.Id);
    }

    [Fact]
    public async Task CreateBoard_ShouldStampCanWriteTrue_ForTheCreator()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto($"canwrite-created-{Guid.NewGuid():N}", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<BoardDto>();
        created!.CanWrite.Should().BeTrue();
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
