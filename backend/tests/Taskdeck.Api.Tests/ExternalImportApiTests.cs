using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ExternalImportApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public ExternalImportApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ExternalImportEndpoint_ShouldReturnUnauthorized_WhenNoToken()
    {
        var boardId = Guid.NewGuid();
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: "Display Name,Company\nAlice Example,Acme",
            TargetColumnName: "Imported",
            DryRun: true);

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request));
    }

    [Fact]
    public async Task DryRun_ShouldReturnConflictDetails_ForDuplicateInputRecords()
    {
        await EnsureAuthenticatedAsync();
        var boardId = await CreateBoardAsync("external-import-dryrun");
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address
                     Alice Example,Acme,alice@example.com
                     Alice Duplicate,Acme,alice@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var response = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExternalImportResultDto>();
        result.Should().NotBeNull();
        result!.RowsCreated.Should().Be(1);
        result.RowsUpdated.Should().Be(0);
        result.RowsSkipped.Should().Be(0);
        result.HasConflicts.Should().BeTrue();
        result.Conflicts.Should().ContainSingle(conflict => conflict.Code == "DuplicateInputRecord");
    }

    [Fact]
    public async Task Apply_ShouldCreateAndThenUpdateRecords_WithDeterministicDedupe()
    {
        await EnsureAuthenticatedAsync();
        var boardId = await CreateBoardAsync("external-import-apply");

        var initialApply = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address,Role
                     Alice Example,Acme,alice@example.com,Engineer
                     Bob Example,Acme,bob@example.com,Designer
                     """,
            TargetColumnName: "Imported",
            DryRun: false);

        var initialResponse = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", initialApply);
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialResult = await initialResponse.Content.ReadFromJsonAsync<ExternalImportResultDto>();
        initialResult.Should().NotBeNull();
        initialResult!.Applied.Should().BeTrue();
        initialResult.RowsCreated.Should().Be(2);
        initialResult.RowsUpdated.Should().Be(0);
        initialResult.RowsSkipped.Should().Be(0);
        initialResult.HasConflicts.Should().BeFalse();

        var updateApply = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address,Role
                     Alice Example,Acme,alice@example.com,Engineer
                     Bob Example,Acme,bob@example.com,Product Designer
                     """,
            TargetColumnName: "Imported",
            DryRun: false);

        var updateResponse = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", updateApply);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateResult = await updateResponse.Content.ReadFromJsonAsync<ExternalImportResultDto>();
        updateResult.Should().NotBeNull();
        updateResult!.Applied.Should().BeTrue();
        updateResult.RowsCreated.Should().Be(0);
        updateResult.RowsUpdated.Should().Be(1);
        updateResult.RowsSkipped.Should().Be(1);
        updateResult.HasConflicts.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_ShouldRollback_WhenOneRecordFailsValidation()
    {
        await EnsureAuthenticatedAsync();
        var boardId = await CreateBoardAsync("external-import-rollback");

        var overlyLongName = new string('A', 250);
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: $"""
                      Display Name,Company,Email Address
                      Alice Example,Acme,alice@example.com
                      {overlyLongName},Acme,bad@example.com
                      """,
            TargetColumnName: "Imported",
            DryRun: false);

        var response = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var boardExportResponse = await _client.GetAsync($"/api/export/boards/{boardId}");
        boardExportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await boardExportResponse.Content.ReadFromJsonAsync<ExportBoardDto>();
        export.Should().NotBeNull();
        export!.Cards.Should().BeEmpty();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "external-import-suite");
        _isAuthenticated = true;
    }

    private async Task<Guid> CreateBoardAsync(string stem)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                Name: $"{stem}-{Guid.NewGuid():N}",
                Description: "External import test board",
                Columns:
                [
                    new ImportColumnDto("Imported", 0, null),
                    new ImportColumnDto("Backlog", 1, null)
                ],
                Cards: Array.Empty<ImportCardDto>(),
                Labels: Array.Empty<ImportLabelDto>()));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.BoardId.Should().NotBeNull();
        return importResult.BoardId!.Value;
    }
}
