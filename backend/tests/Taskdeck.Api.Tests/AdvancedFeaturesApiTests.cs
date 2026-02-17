using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AdvancedFeaturesApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public AdvancedFeaturesApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BoardAccessAndImportEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var accessId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/import/boards?userId={userId}",
                new ImportBoardDto("Unauthorized", null, Array.Empty<ImportColumnDto>(), Array.Empty<ImportCardDto>(), Array.Empty<ImportLabelDto>())));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/import/boards/json?userId={userId}",
                new ExportBoardDto(
                    new BoardDto(Guid.NewGuid(), "Unauthorized", null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                    Array.Empty<ColumnDto>(),
                    Array.Empty<CardDto>(),
                    Array.Empty<LabelDto>(),
                    new List<BoardAccessDto>(),
                    DateTimeOffset.UtcNow,
                    "test")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/boards/{boardId}/access"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/access",
                new GrantAccessDto(boardId, userId, UserRole.Viewer)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PutAsJsonAsync(
                $"/api/boards/{boardId}/access/{accessId}?updatedBy={userId}",
                new UpdateAccessDto(UserRole.Editor)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.DeleteAsync($"/api/boards/{boardId}/access/{accessId}?revokedBy={userId}"));
    }

    [Fact]
    public async Task Login_ShouldReturnToken_ForRegisteredUser()
    {
        var (username, email, password, _) = await RegisterUserAsync("authlogin");

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto(username, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();
        payload!.Token.Should().NotBeNullOrWhiteSpace();
        payload.User.Email.Should().Be(email);
    }

    [Fact]
    public async Task ImportBoardFromJson_ShouldAcceptExportPayloadShape()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, importingUserId) = await RegisterUserAsync("importer");
        var now = DateTimeOffset.UtcNow;
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var labelId = Guid.NewGuid();

        var exportPayload = new ExportBoardDto(
            new BoardDto(boardId, "Imported from Export", "description", false, now, now),
            new[] { new ColumnDto(columnId, boardId, "Todo", 0, null, 1, now, now) },
            new[]
            {
                new CardDto(
                    Guid.NewGuid(),
                    boardId,
                    columnId,
                    "Imported Card",
                    string.Empty,
                    null,
                    false,
                    null,
                    0,
                    new List<LabelDto> { new LabelDto(labelId, boardId, "Bug", "#FF0000", now, now) },
                    now,
                    now)
            },
            new[] { new LabelDto(labelId, boardId, "Bug", "#FF0000", now, now) },
            new List<BoardAccessDto>(),
            now,
            "exporter");

        var response = await _client.PostAsJsonAsync($"/api/import/boards/json?userId={importingUserId}", exportPayload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var importResult = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.ColumnsImported.Should().Be(1);
        importResult.CardsImported.Should().Be(1);
        importResult.LabelsImported.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAccess_ShouldReturnNotFound_WhenAccessBelongsToDifferentBoard()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, granterId) = await RegisterUserAsync("granter");
        var (_, _, _, targetUserId) = await RegisterUserAsync("target");
        var board1Id = await CreateOwnedBoardAsync("board1", granterId);
        var board2Id = await CreateOwnedBoardAsync("board2", granterId);

        var grantResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board2Id}/access?grantedBy={granterId}",
            new GrantAccessDto(board2Id, targetUserId, UserRole.Viewer));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdAccess = await grantResponse.Content.ReadFromJsonAsync<BoardAccessDto>();
        createdAccess.Should().NotBeNull();

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/boards/{board1Id}/access/{createdAccess!.Id}?updatedBy={granterId}",
            new UpdateAccessDto(UserRole.Editor));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var errorPayload = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    private async Task<(string Username, string Email, string Password, Guid UserId)> RegisterUserAsync(string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";
        const string password = "password123";

        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();

        return (username, email, password, payload!.User.Id);
    }

    private async Task<Guid> CreateOwnedBoardAsync(string stem, Guid ownerId)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/import/boards?userId={ownerId}",
            new ImportBoardDto(
                $"{stem}-{Guid.NewGuid():N}",
                null,
                Array.Empty<ImportColumnDto>(),
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "advanced-suite");
        _isAuthenticated = true;
    }
}
