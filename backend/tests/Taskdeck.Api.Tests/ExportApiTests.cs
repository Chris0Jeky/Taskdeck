using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ExportApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public ExportApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ExportEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var boardId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/export/boards/{boardId}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/export/boards/{boardId}/json"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                "/api/import/boards",
                new ImportBoardDto("Unauthorized", null, Array.Empty<ImportColumnDto>(), Array.Empty<ImportCardDto>(), Array.Empty<ImportLabelDto>())));

        using var document = JsonDocument.Parse("""{"board":{"id":"00000000-0000-0000-0000-000000000000","name":"x","isArchived":false,"createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z"},"columns":[],"cards":[],"labels":[],"accesses":[],"exportedAt":"2026-01-01T00:00:00Z","exportedBy":"test"}""");
        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync("/api/import/boards/json", document.RootElement));
    }

    [Fact]
    public async Task ExportBoard_ShouldReturnBoardData_WhenBoardExists()
    {
        await EnsureAuthenticatedAsync();
        var boardId = await CreateOwnedBoardAsync("export");

        var response = await _client.GetAsync($"/api/export/boards/{boardId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var export = await response.Content.ReadFromJsonAsync<ExportBoardDto>();
        export.Should().NotBeNull();
        export!.Board.Should().NotBeNull();
        export.Board.Id.Should().Be(boardId);
    }

    [Fact]
    public async Task ExportBoardAsJson_ShouldReturnJsonString()
    {
        await EnsureAuthenticatedAsync();
        var boardId = await CreateOwnedBoardAsync("jsonexport");

        var response = await _client.GetAsync($"/api/export/boards/{boardId}/json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();

        // The response should be valid JSON
        var action = () => JsonDocument.Parse(content);
        action.Should().NotThrow();
    }

    [Fact]
    public async Task ImportBoard_ShouldCreateNewBoard()
    {
        await EnsureAuthenticatedAsync();

        var importDto = new ImportBoardDto(
            $"Imported-{Guid.NewGuid():N}",
            "Imported board description",
            new[]
            {
                new ImportColumnDto("To Do", 0, null),
                new ImportColumnDto("Done", 1, null)
            },
            new[]
            {
                new ImportCardDto("Task 1", "Description", "To Do", 0, null, null)
            },
            Array.Empty<ImportLabelDto>());

        var response = await _client.PostAsJsonAsync("/api/import/boards", importDto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.BoardId.Should().NotBeNull();
        result.ColumnsImported.Should().Be(2);
        result.CardsImported.Should().Be(1);
    }

    [Fact]
    public async Task ExportThenImport_ShouldRoundTrip()
    {
        await EnsureAuthenticatedAsync();

        // Create a board with content via import
        var importDto = new ImportBoardDto(
            $"RoundTrip-{Guid.NewGuid():N}",
            "Round-trip test",
            new[] { new ImportColumnDto("Backlog", 0, 5) },
            new[] { new ImportCardDto("Card A", "desc", "Backlog", 0, null, null) },
            new[] { new ImportLabelDto("Bug", "#FF0000") });

        var importResponse = await _client.PostAsJsonAsync("/api/import/boards", importDto);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResultDto>();
        importResult.Should().NotBeNull();
        var boardId = importResult!.BoardId!.Value;

        // Export the board as JSON
        var exportResponse = await _client.GetAsync($"/api/export/boards/{boardId}/json");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportJson = await exportResponse.Content.ReadAsStringAsync();

        // Import from the exported JSON
        var reimportResponse = await _client.PostAsync(
            "/api/import/boards/json",
            new StringContent(exportJson, System.Text.Encoding.UTF8, "application/json"));
        reimportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reimportResult = await reimportResponse.Content.ReadFromJsonAsync<ImportResultDto>();
        reimportResult.Should().NotBeNull();
        reimportResult!.Success.Should().BeTrue();
        reimportResult.ColumnsImported.Should().Be(1);
        reimportResult.CardsImported.Should().Be(1);
        reimportResult.LabelsImported.Should().Be(1);
    }

    [Fact]
    public async Task ExportBoard_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        await EnsureAuthenticatedAsync();
        var boardId = await CreateOwnedBoardAsync("forbidden-export");

        await ApiTestHarness.AuthenticateAsync(_client, "export-forbidden-user");
        _isAuthenticated = true;

        var response = await _client.GetAsync($"/api/export/boards/{boardId}");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private async Task<Guid> CreateOwnedBoardAsync(string stem)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/import/boards",
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

        await ApiTestHarness.AuthenticateAsync(_client, "export-suite");
        _isAuthenticated = true;
    }
}
