using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ExportApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExportApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ExportBoard_ShouldReturnBoardData_WhenBoardExists()
    {
        var (_, _, _, userId) = await RegisterUserAsync("exporter");
        var boardId = await CreateOwnedBoardAsync("export", userId);

        var response = await _client.GetAsync($"/api/export/boards/{boardId}?userId={userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var export = await response.Content.ReadFromJsonAsync<ExportBoardDto>();
        export.Should().NotBeNull();
        export!.Board.Should().NotBeNull();
        export.Board.Id.Should().Be(boardId);
    }

    [Fact]
    public async Task ExportBoardAsJson_ShouldReturnJsonString()
    {
        var (_, _, _, userId) = await RegisterUserAsync("jsonexp");
        var boardId = await CreateOwnedBoardAsync("jsonexport", userId);

        var response = await _client.GetAsync($"/api/export/boards/{boardId}/json?userId={userId}");

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
        var (_, _, _, userId) = await RegisterUserAsync("importer");

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

        var response = await _client.PostAsJsonAsync($"/api/import/boards?userId={userId}", importDto);

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
        var (_, _, _, userId) = await RegisterUserAsync("roundtrip");

        // Create a board with content via import
        var importDto = new ImportBoardDto(
            $"RoundTrip-{Guid.NewGuid():N}",
            "Round-trip test",
            new[] { new ImportColumnDto("Backlog", 0, 5) },
            new[] { new ImportCardDto("Card A", "desc", "Backlog", 0, null, null) },
            new[] { new ImportLabelDto("Bug", "#FF0000") });

        var importResponse = await _client.PostAsJsonAsync($"/api/import/boards?userId={userId}", importDto);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResultDto>();
        importResult.Should().NotBeNull();
        var boardId = importResult!.BoardId!.Value;

        // Export the board as JSON
        var exportResponse = await _client.GetAsync($"/api/export/boards/{boardId}/json?userId={userId}");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var exportJson = await exportResponse.Content.ReadAsStringAsync();

        // Import from the exported JSON
        var reimportResponse = await _client.PostAsync(
            $"/api/import/boards/json?userId={userId}",
            new StringContent(exportJson, System.Text.Encoding.UTF8, "application/json"));
        reimportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reimportResult = await reimportResponse.Content.ReadFromJsonAsync<ImportResultDto>();
        reimportResult.Should().NotBeNull();
        reimportResult!.Success.Should().BeTrue();
        reimportResult.ColumnsImported.Should().Be(1);
        reimportResult.CardsImported.Should().Be(1);
        reimportResult.LabelsImported.Should().Be(1);
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
}
