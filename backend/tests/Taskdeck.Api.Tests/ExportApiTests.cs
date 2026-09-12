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

    [Theory]
    [InlineData("/api/import/boards")]
    [InlineData("/api/import/boards/json")]
    [InlineData("/api/import/boards/preview")]
    public async Task EstimatedEffort_AllImportRoutesRejectOutOfRangeAndNonIntegerPayloads(string route)
    {
        await EnsureAuthenticatedAsync();
        var name = $"Rejected-estimate-{Guid.NewGuid():N}";
        foreach (var rawEstimate in new[] { "-1", "1000001", "1.5", "true", "\"not-minutes\"" })
        {
            var json = $$"""{"name":"{{name}}","columns":[{"name":"Work","position":0}],"cards":[{"title":"Valid first","columnName":"Work","position":0},{"title":"Invalid later","columnName":"Work","position":1,"estimatedEffortMinutes":{{rawEstimate}}}],"labels":[]}""";
            using var response = await _client.PostAsync(route,
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{route} must reject {rawEstimate}");
        }
        var boards = (await _client.GetFromJsonAsync<PaginatedResult<BoardDto>>($"/api/boards?search={name}"))!;
        boards.TotalCount.Should().Be(0, "no failed import may leave a board behind");
    }

    [Fact]
    public async Task EstimatedEffort_RealDatabaseRoundTripPreservesActiveAndArchivedNullableValues()
    {
        await EnsureAuthenticatedAsync();
        var cards = new List<ImportCardDto>();
        foreach (var archived in new[] { false, true })
        foreach (var minutes in new int?[] { null, 0, 135 })
            cards.Add(new($"{archived}-{minutes?.ToString() ?? "unknown"}", null, "Work", cards.Count, null, [],
                SourceId: Guid.NewGuid(), IsArchived: archived, EstimatedEffortMinutes: minutes));
        var payload = new ImportBoardDto("Estimate round trip", null, [new("Work", 0, null)], cards, []);
        var first = await _client.PostAsJsonAsync("/api/import/boards", payload);
        first.EnsureSuccessStatusCode();
        var firstId = (await first.Content.ReadFromJsonAsync<ImportResultDto>())!.BoardId!.Value;
        var json = await _client.GetStringAsync($"/api/export/boards/{firstId}/json");
        var second = await _client.PostAsync("/api/import/boards/json",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        second.EnsureSuccessStatusCode();
        var secondId = (await second.Content.ReadFromJsonAsync<ImportResultDto>())!.BoardId!.Value;
        foreach (var archived in new[] { false, true })
        {
            var route = $"/api/boards/{secondId}/cards" + (archived ? "/archived" : "");
            var imported = (await _client.GetFromJsonAsync<List<CardDto>>(route))!;
            imported.Should().HaveCount(3);
            foreach (var card in cards.Where(card => card.IsArchived == archived))
                imported.Single(copy => copy.Title == card.Title).EstimatedEffortMinutes.Should().Be(card.EstimatedEffortMinutes);
        }
    }

    [Fact]
    public async Task ArchivedCards_RoundTripWithoutRevival_AndKeepFreshImportIds()
    {
        await EnsureAuthenticatedAsync();
        var sourceId = Guid.NewGuid();
        var payload = new ImportBoardDto("Archive portability", null,
            [new ImportColumnDto("Original", 0, null)],
            [new ImportCardDto("Historical", "Evidence", "Original", 4, null, ["Retained"], SourceId: sourceId, IsArchived: true)],
            [new ImportLabelDto("Retained", "#123456")]);
        var imported = await _client.PostAsJsonAsync("/api/import/boards", payload);
        imported.StatusCode.Should().Be(HttpStatusCode.OK);
        var boardId = (await imported.Content.ReadFromJsonAsync<ImportResultDto>())!.BoardId!.Value;
        (await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{boardId}/cards"))!.Should().BeEmpty();
        var historical = (await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{boardId}/cards/archived"))!.Single();
        historical.Id.Should().NotBe(sourceId);
        historical.Labels.Should().ContainSingle().Which.Name.Should().Be("Retained");
        var exported = await _client.GetAsync($"/api/export/boards/{boardId}/json");
        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        var reimported = await _client.PostAsync("/api/import/boards/json",
            new StringContent(await exported.Content.ReadAsStringAsync(), System.Text.Encoding.UTF8, "application/json"));
        reimported.StatusCode.Should().Be(HttpStatusCode.OK);
        var newBoardId = (await reimported.Content.ReadFromJsonAsync<ImportResultDto>())!.BoardId!.Value;
        (await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{newBoardId}/cards"))!.Should().BeEmpty();
        var restored = (await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{newBoardId}/cards/archived"))!.Single();
        restored.Id.Should().NotBe(historical.Id);
        restored.Position.Should().Be(4);
        restored.IsArchived.Should().BeTrue();
        restored.Labels.Should().ContainSingle().Which.Name.Should().Be("Retained");
    }

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

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/export/database"));

        using var databaseImportContent = CreateDatabaseImportContent(CreateSqlitePayload());
        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync("/api/import/database", databaseImportContent));
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

    [Fact]
    public async Task DatabaseEndpoints_ShouldReturnForbidden_WhenSandboxIsDisabled()
    {
        await EnsureAuthenticatedAsync();

        await ApiTestHarness.AssertForbiddenAsync(
            await _client.GetAsync("/api/export/database"));

        using var importContent = CreateDatabaseImportContent(CreateSqlitePayload());
        await ApiTestHarness.AssertForbiddenAsync(
            await _client.PostAsync("/api/import/database", importContent));
    }

    [Fact]
    public async Task ImportDatabase_ShouldReturnBadRequest_WhenFileIsMissing()
    {
        await EnsureAuthenticatedAsync();

        using var content = new MultipartFormDataContent();
        var response = await _client.PostAsync("/api/import/database", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private static MultipartFormDataContent CreateDatabaseImportContent(byte[] payload)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(payload), "file", "taskdeck.db");
        return content;
    }

    private static byte[] CreateSqlitePayload()
    {
        var payload = new byte[128];
        var signature = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
        Array.Copy(signature, payload, signature.Length);
        return payload;
    }
}
