using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ExternalImportApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;
    private bool _isAuthenticated;

    public ExternalImportApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
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
        result.Conflicts.Should().ContainSingle(conflict =>
            conflict.Code == "DuplicateInputRecord" &&
            conflict.IncomingValue == "email:alice@example.com");
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
    public async Task Apply_ShouldReturnConflictWithoutMutation_WhenOneRecordViolatesCardConstraints()
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

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var result = await response.Content.ReadFromJsonAsync<ExternalImportResultDto>();
        result.Should().NotBeNull();
        result!.Applied.Should().BeFalse();
        result.HasConflicts.Should().BeTrue();
        result.Conflicts.Should().Contain(conflict =>
            conflict.Code == "TitleTooLong" &&
            conflict.Path == "$.rows[3].title");

        var boardExportResponse = await _client.GetAsync($"/api/export/boards/{boardId}");
        boardExportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await boardExportResponse.Content.ReadFromJsonAsync<ExportBoardDto>();
        export.Should().NotBeNull();
        export!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ShouldReturnValidationError_WhenExplicitCsvMappingHeaderDoesNotExist()
    {
        await EnsureAuthenticatedAsync();
        var boardId = await CreateBoardAsync("external-import-invalid-mapping");
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address
                     Alice Example,Acme,alice@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: true,
            Csv: new ExternalImportCsvOptionsDto(EmailColumn: "Email Typo"));

        var response = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        payload.GetProperty("message").GetString().Should().Contain("Email Typo");
        payload.GetProperty("message").GetString().Should().Contain("emailColumn");
    }

    [Fact]
    public async Task DryRun_ShouldIncludeConflictValues_ForAmbiguousExistingBoardMatches()
    {
        await EnsureAuthenticatedAsync();

        const string duplicateKey = "email:alice@example.com";
        var boardId = await CreateBoardAsync(
            "external-import-ambiguous",
            [
                new ImportCardDto(
                    "Alice Existing One",
                    $"[taskdeck-import-meta] {{\"provider\":\"csv\",\"profile\":\"outreach.contacts.v1\",\"dedupeKey\":\"{duplicateKey}\"}}",
                    "Imported",
                    0,
                    null,
                    null),
                new ImportCardDto(
                    "Alice Existing Two",
                    $"[taskdeck-import-meta] {{\"provider\":\"csv\",\"profile\":\"outreach.contacts.v1\",\"dedupeKey\":\"{duplicateKey}\"}}",
                    "Backlog",
                    1,
                    null,
                    null)
            ]);

        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address
                     Alice Incoming,Acme,alice@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var response = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExternalImportResultDto>();
        result.Should().NotBeNull();
        result!.HasConflicts.Should().BeTrue();
        result.Conflicts.Should().Contain(conflict =>
            conflict.Code == "ExistingDuplicateDedupeKey" &&
            conflict.IncomingValue == duplicateKey &&
            conflict.ExistingValue != null &&
            conflict.ExistingValue.Contains("Alice Existing One", StringComparison.Ordinal) &&
            conflict.ExistingValue.Contains("Alice Existing Two", StringComparison.Ordinal));
        result.Conflicts.Should().Contain(conflict =>
            conflict.Code == "AmbiguousExistingMatch" &&
            conflict.Path == "$.rows[2]" &&
            conflict.IncomingValue == duplicateKey &&
            conflict.ExistingValue != null &&
            conflict.ExistingValue.Contains("Alice Existing One", StringComparison.Ordinal) &&
            conflict.ExistingValue.Contains("Alice Existing Two", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_ShouldNotUpdateRecords_WhenExistingMetadataHasDifferentProviderOrProfile()
    {
        await EnsureAuthenticatedAsync();

        const string dedupeKey = "email:alice@example.com";
        var boardId = await CreateBoardAsync(
            "external-import-provider-scope",
            [
                new ImportCardDto(
                    "Alice Existing",
                    $"[taskdeck-import-meta] {{\"provider\":\"other-provider\",\"profile\":\"other.profile.v1\",\"dedupeKey\":\"{dedupeKey}\"}}",
                    "Imported",
                    0,
                    null,
                    null)
            ]);

        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address,Role
                     Alice Incoming,Acme,alice@example.com,Engineer
                     """,
            TargetColumnName: "Imported",
            DryRun: false);

        var response = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExternalImportResultDto>();
        result.Should().NotBeNull();
        result!.Applied.Should().BeTrue();
        result.RowsCreated.Should().Be(1);
        result.RowsUpdated.Should().Be(0);
        result.RowsSkipped.Should().Be(0);

        var boardExportResponse = await _client.GetAsync($"/api/export/boards/{boardId}");
        boardExportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await boardExportResponse.Content.ReadFromJsonAsync<ExportBoardDto>();
        export.Should().NotBeNull();
        export!.Cards.Should().HaveCount(2);
        export.Cards.Should().Contain(card => card.Title == "Alice Existing");
        export.Cards.Should().Contain(card => card.Title == "Alice Incoming");
    }

    [Fact]
    public async Task Apply_ShouldReturnWipLimitExceeded_WhenTargetColumnIsFull()
    {
        await EnsureAuthenticatedAsync();

        var boardId = await CreateBoardAsync(
            "external-import-wip-limit",
            cards:
            [
                new ImportCardDto(
                    "Already In Imported",
                    "existing",
                    "Imported",
                    0,
                    null,
                    null)
            ],
            importedColumnWipLimit: 1);

        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address
                     Alice Incoming,Acme,alice@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: false);

        var response = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "WipLimitExceeded");
    }

    [Fact]
    public async Task Apply_ShouldAllowInPlaceUpdate_WhenTargetColumnIsAtWipLimit()
    {
        await EnsureAuthenticatedAsync();

        const string dedupeKey = "email:alice@example.com";
        var boardId = await CreateBoardAsync(
            "external-import-wip-limit-update",
            cards:
            [
                new ImportCardDto(
                    "Alice Existing",
                    $"[taskdeck-import-meta] {{\"provider\":\"csv\",\"profile\":\"outreach.contacts.v1\",\"dedupeKey\":\"{dedupeKey}\"}}\n\nDisplay Name: Alice Existing\nCompany: Acme\nRole: Engineer\nEmail: alice@example.com\nLinkedIn: \nLast Touch At: ",
                    "Imported",
                    0,
                    null,
                    null)
            ],
            importedColumnWipLimit: 1);

        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address,Role
                     Alice Existing,Acme,alice@example.com,Principal Engineer
                     """,
            TargetColumnName: "Imported",
            DryRun: false);

        var response = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExternalImportResultDto>();
        result.Should().NotBeNull();
        result!.Applied.Should().BeTrue();
        result.RowsCreated.Should().Be(0);
        result.RowsUpdated.Should().Be(1);
        result.RowsSkipped.Should().Be(0);
    }

    [Fact]
    public async Task Apply_ShouldNotBlockUnrelatedImport_WhenBoardHasHistoricalDuplicateDedupeKey()
    {
        await EnsureAuthenticatedAsync();

        const string historicalDuplicateKey = "email:historical@example.com";
        var boardId = await CreateBoardAsync(
            "external-import-unrelated-duplicate",
            cards:
            [
                new ImportCardDto(
                    "Historical One",
                    $"[taskdeck-import-meta] {{\"provider\":\"csv\",\"profile\":\"outreach.contacts.v1\",\"dedupeKey\":\"{historicalDuplicateKey}\"}}",
                    "Imported",
                    0,
                    null,
                    null),
                new ImportCardDto(
                    "Historical Two",
                    $"[taskdeck-import-meta] {{\"provider\":\"csv\",\"profile\":\"outreach.contacts.v1\",\"dedupeKey\":\"{historicalDuplicateKey}\"}}",
                    "Backlog",
                    1,
                    null,
                    null)
            ]);

        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address
                     New Contact,Acme,new.contact@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: false);

        var response = await _client.PostAsJsonAsync($"/api/boards/{boardId}/imports/external", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExternalImportResultDto>();
        result.Should().NotBeNull();
        result!.Applied.Should().BeTrue();
        result.RowsCreated.Should().Be(1);
        result.RowsUpdated.Should().Be(0);
        result.RowsSkipped.Should().Be(0);
        result.Conflicts.Should().NotContain(conflict => conflict.Code == "ExistingDuplicateDedupeKey");
        result.Conflicts.Should().NotContain(conflict => conflict.Code == "AmbiguousExistingMatch");
    }

    [Fact]
    public async Task ImportEndpoint_ShouldReturnForbidden_ForBothForeignAndMissingBoards()
    {
        await EnsureAuthenticatedAsync();
        var foreignBoardId = await CreateBoardAsync("external-import-foreign-board");

        var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "external-import-other-user");

        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: "Display Name,Company\nAlice Example,Acme",
            TargetColumnName: "Imported",
            DryRun: true);

        var foreignResponse = await otherClient.PostAsJsonAsync($"/api/boards/{foreignBoardId}/imports/external", request);
        var missingResponse = await otherClient.PostAsJsonAsync($"/api/boards/{Guid.NewGuid()}/imports/external", request);

        await ApiTestHarness.AssertForbiddenAsync(foreignResponse);
        await ApiTestHarness.AssertForbiddenAsync(missingResponse);
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

    private async Task<Guid> CreateBoardAsync(
        string stem,
        IEnumerable<ImportCardDto>? cards = null,
        int? importedColumnWipLimit = null,
        int? backlogColumnWipLimit = null)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                Name: $"{stem}-{Guid.NewGuid():N}",
                Description: "External import test board",
                Columns:
                [
                    new ImportColumnDto("Imported", 0, importedColumnWipLimit),
                    new ImportColumnDto("Backlog", 1, backlogColumnWipLimit)
                ],
                Cards: cards ?? Array.Empty<ImportCardDto>(),
                Labels: Array.Empty<ImportLabelDto>()));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.BoardId.Should().NotBeNull();
        return importResult.BoardId!.Value;
    }
}
