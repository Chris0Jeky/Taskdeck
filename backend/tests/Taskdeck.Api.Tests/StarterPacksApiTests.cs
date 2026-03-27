using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class StarterPacksApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public StarterPacksApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetStarterPackCatalog_ShouldReturnUnauthorized_WhenUnauthenticated()
    {
        var response = await _client.GetAsync($"/api/boards/{Guid.NewGuid()}/starter-packs/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetStarterPackCatalog_ShouldReturnFirstPartyPacks_WhenBoardReadable()
    {
        var board = await CreateBoardAsync();

        var response = await _client.GetAsync($"/api/boards/{board.Id}/starter-packs/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<List<StarterPackCatalogEntryDto>>();
        var catalog = payload ?? throw new InvalidOperationException("Starter-pack catalog response payload should not be null.");
        catalog.Should().OnlyHaveUniqueItems(entry => entry.Id);
        catalog.Count(entry => entry.Category == StarterPackCatalogCategories.LabelPack).Should().BeGreaterThanOrEqualTo(1);
        catalog.Count(entry => entry.Category == StarterPackCatalogCategories.ColumnFlow).Should().BeGreaterThanOrEqualTo(1);
        catalog.Count(entry => entry.Category == StarterPackCatalogCategories.BoardBlueprint).Should().Be(4);
        catalog.Should().ContainSingle(entry => entry.Id == "board-blueprint-client-onboarding");
    }

    [Fact]
    public async Task GetStarterPackCatalog_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        var board = await CreateBoardAsync();

        await ApiTestHarness.AuthenticateAsync(_client, "starter-pack-other-user");
        _isAuthenticated = true;

        var response = await _client.GetAsync($"/api/boards/{board.Id}/starter-packs/catalog");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        var board = await CreateBoardAsync();
        await ApiTestHarness.AuthenticateAsync(_client, "starter-pack-apply-outsider");
        _isAuthenticated = true;

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            new ApplyStarterPackDto(BuildCanonicalManifest(), DryRun: false));

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{Guid.NewGuid()}/starter-packs/apply",
            new ApplyStarterPackDto(BuildCanonicalManifest(), DryRun: false));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldCreateBoardArtifacts_WhenManifestIsValid()
    {
        var board = await CreateBoardAsync();
        var request = new ApplyStarterPackDto(BuildCanonicalManifest(), DryRun: false);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeTrue();
        payload.HasConflicts.Should().BeFalse();

        var labelsResponse = await _client.GetAsync($"/api/boards/{board.Id}/labels");
        labelsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var labels = await labelsResponse.Content.ReadFromJsonAsync<List<LabelDto>>();
        labels.Should().NotBeNull();
        labels!.Should().ContainSingle(label => label.Name == "priority-high");

        var columnsResponse = await _client.GetAsync($"/api/boards/{board.Id}/columns");
        columnsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var columns = await columnsResponse.Content.ReadFromJsonAsync<List<ColumnDto>>();
        columns.Should().NotBeNull();
        columns!.Should().HaveCount(2);
        columns.Should().Contain(column => column.Name == "Backlog" && column.Position == 0);
        columns.Should().Contain(column => column.Name == "Done" && column.Position == 1);

        var cardsResponse = await _client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().NotBeNull();
        cards!.Should().ContainSingle(card =>
            card.Title == "Set up sprint board" &&
            card.Labels.Any(label => label.Name == "priority-high"));
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldAllowLabelOnlyPack_WhenBoardHasDuplicateColumnNames()
    {
        var board = await CreateBoardAsync();

        var firstColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "duplicate-column", 0, null));
        firstColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Duplicate-Column", 1, null));
        secondColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            new ApplyStarterPackDto(BuildLabelOnlyManifest(), DryRun: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeTrue();
        payload.HasBlockingConflicts.Should().BeFalse();
        payload.Conflicts.Should().NotContain(conflict =>
            conflict.Code == "ExistingColumnNameConflict" ||
            conflict.Code == "ExistingColumnPositionConflict");

        var labels = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        labels.Should().NotBeNull();
        labels!.Should().ContainSingle(label => label.Name == "priority-high");
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldAllowColumnOnlyPack_WhenBoardHasDuplicateLabelNames()
    {
        var board = await CreateBoardAsync();

        var firstLabelResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "duplicate-label", "#111111"));
        firstLabelResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondLabelResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "Duplicate-Label", "#222222"));
        secondLabelResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            new ApplyStarterPackDto(BuildColumnOnlyManifest(), DryRun: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeTrue();
        payload.HasBlockingConflicts.Should().BeFalse();
        payload.Conflicts.Should().NotContain(conflict => conflict.Code == "ExistingLabelNameConflict");

        var columns = await _client.GetFromJsonAsync<List<ColumnDto>>($"/api/boards/{board.Id}/columns");
        columns.Should().NotBeNull();
        columns!.Should().ContainSingle(column => column.Name == "Backlog" && column.Position == 0);
        columns.Should().ContainSingle(column => column.Name == "Done" && column.Position == 1);
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldAllowCanonicalPack_WhenBoardHasUnrelatedDuplicateLabelNames()
    {
        var board = await CreateBoardAsync();

        var firstLabelResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "legacy-label", "#111111"));
        firstLabelResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondLabelResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "Legacy-Label", "#222222"));
        secondLabelResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            new ApplyStarterPackDto(BuildCanonicalManifest(), DryRun: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeTrue();
        payload.HasBlockingConflicts.Should().BeFalse();
        payload.Conflicts.Should().NotContain(conflict =>
            conflict.Code == "ExistingLabelNameConflict" &&
            string.Equals(conflict.IncomingValue, "legacy-label", StringComparison.OrdinalIgnoreCase));

        var labels = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        labels.Should().NotBeNull();
        labels!.Should().ContainSingle(label => label.Name == "priority-high");
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldAllowCanonicalPack_WhenBoardHasUnrelatedDuplicateColumnNames()
    {
        var board = await CreateBoardAsync();

        var firstColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "legacy-column", 10, null));
        firstColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Legacy-Column", 11, null));
        secondColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            new ApplyStarterPackDto(BuildCanonicalManifest(), DryRun: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeTrue();
        payload.HasBlockingConflicts.Should().BeFalse();
        payload.Conflicts.Should().NotContain(conflict =>
            conflict.Code == "ExistingColumnNameConflict" &&
            string.Equals(conflict.IncomingValue, "legacy-column", StringComparison.OrdinalIgnoreCase));

        var columns = await _client.GetFromJsonAsync<List<ColumnDto>>($"/api/boards/{board.Id}/columns");
        columns.Should().NotBeNull();
        columns!.Should().ContainSingle(column => column.Name == "Backlog" && column.Position == 0);
        columns.Should().ContainSingle(column => column.Name == "Done" && column.Position == 1);
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldBeIdempotent_WhenReapplied()
    {
        var board = await CreateBoardAsync();
        var request = new ApplyStarterPackDto(BuildCanonicalManifest(), DryRun: false);

        var firstApplyResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            request);
        firstApplyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondApplyResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            request);

        secondApplyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await secondApplyResponse.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeTrue();
        payload.HasConflicts.Should().BeTrue();
        payload.HasBlockingConflicts.Should().BeFalse();
        payload.Actions.Should().Contain(action =>
            action.EntityType == "label" &&
            action.Operation == "skip" &&
            action.Key == "priority-high");
        payload.Actions.Should().Contain(action =>
            action.EntityType == "column" &&
            action.Operation == "skip" &&
            action.Key == "Backlog");
        payload.Actions.Should().Contain(action =>
            action.EntityType == "seedCard" &&
            action.Operation == "skip" &&
            action.Key.Contains("Set up sprint board", StringComparison.Ordinal));
        payload.Conflicts.Should().Contain(conflict =>
            conflict.Code == "SeedCardAlreadyExistsConflict" &&
            conflict.Severity == StarterPackConflictSeverity.Warning);

        var labels = await _client.GetFromJsonAsync<List<LabelDto>>($"/api/boards/{board.Id}/labels");
        labels.Should().NotBeNull();
        labels!.Count(label => string.Equals(label.Name, "priority-high", StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(1);

        var columns = await _client.GetFromJsonAsync<List<ColumnDto>>($"/api/boards/{board.Id}/columns");
        columns.Should().NotBeNull();
        columns!.Should().HaveCount(2);

        var cards = await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards");
        cards.Should().NotBeNull();
        cards!.Count(card => string.Equals(card.Title, "Set up sprint board", StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task ApplyStarterPack_DryRun_ShouldReturnActionableConflictReport()
    {
        var board = await CreateBoardAsync();
        var existingColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Existing", 0, null));
        existingColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var dryRunRequest = new ApplyStarterPackDto(BuildPositionConflictManifest(), DryRun: true);
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            dryRunRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeFalse();
        payload.HasConflicts.Should().BeTrue();
        payload.Conflicts.Should().Contain(conflict =>
            conflict.Code == "ColumnPositionConflict" &&
            conflict.Path == "$.columns[0].position" &&
            conflict.ExistingValue == "Existing" &&
            conflict.IncomingValue == "Backlog");
    }

    [Fact]
    public async Task ApplyStarterPack_DryRun_ShouldIncludeSeedCardSkipAction_WhenSeedCardReferencesUnresolvableColumn()
    {
        var board = await CreateBoardAsync();
        var existingColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Existing", 0, null));
        existingColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var applyRequest = new ApplyStarterPackDto(BuildUnresolvableSeedCardManifest(), DryRun: true);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            applyRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeFalse();
        payload.HasConflicts.Should().BeTrue();
        payload.HasBlockingConflicts.Should().BeTrue();
        payload.Conflicts.Should().Contain(conflict =>
            conflict.Code == "ColumnPositionConflict");
        payload.Conflicts.Should().Contain(conflict =>
            conflict.Code == "SeedCardColumnConflict" &&
            conflict.Severity == StarterPackConflictSeverity.Warning);

        payload.Actions.Count(action =>
                action.EntityType == "seedCard" &&
                action.Operation == "skip" &&
                action.Key == "Investigate intake @ Backlog")
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldReturnConflict_WhenApplyHasConflicts()
    {
        var board = await CreateBoardAsync();
        var existingColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Existing", 0, null));
        existingColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var applyRequest = new ApplyStarterPackDto(BuildPositionConflictManifest(), DryRun: false);
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            applyRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeFalse();
        payload.HasConflicts.Should().BeTrue();
        payload.Conflicts.Should().Contain(conflict => conflict.Code == "ColumnPositionConflict");

        var columns = await _client.GetFromJsonAsync<List<ColumnDto>>($"/api/boards/{board.Id}/columns");
        columns.Should().NotBeNull();
        columns!.Should().ContainSingle(column => column.Name == "Existing");
        columns.Should().NotContain(column => column.Name == "Backlog");
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldReturnValidationError_WhenManifestIsEmpty()
    {
        var board = await CreateBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            new ApplyStarterPackDto(BuildEmptyManifest(), DryRun: false));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldReturnValidationError_WhenManifestContainsOnlyTemplates()
    {
        var board = await CreateBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            new ApplyStarterPackDto(BuildTemplateOnlyManifest(), DryRun: false));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task ApplyStarterPack_ShouldReturnConflict_WhenBoardContainsDuplicateNamesUsedByManifest()
    {
        var board = await CreateBoardAsync();

        var firstLabelResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "priority-high", "#111111"));
        firstLabelResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondLabelResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/labels",
            new CreateLabelDto(board.Id, "Priority-High", "#222222"));
        secondLabelResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", 10, null));
        firstColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "backlog", 11, null));
        secondColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var applyResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/starter-packs/apply",
            new ApplyStarterPackDto(BuildCanonicalManifest(), DryRun: false));

        applyResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await applyResponse.Content.ReadFromJsonAsync<StarterPackApplyResultDto>();
        payload.Should().NotBeNull();
        payload!.Applied.Should().BeFalse();
        payload.HasConflicts.Should().BeTrue();
        payload.Conflicts.Should().Contain(conflict =>
            conflict.Code == "ExistingLabelNameConflict" &&
            conflict.Path == "$.board.labels");
        payload.Conflicts.Should().Contain(conflict =>
            conflict.Code == "ExistingColumnNameConflict" &&
            conflict.Path == "$.board.columns");
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "starter-pack-board", "Starter pack integration tests");
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "starter-pack-suite");
        _isAuthenticated = true;
    }

    private static StarterPackManifestDto BuildCanonicalManifest()
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "engineering-onboarding",
            DisplayName = "Engineering Onboarding",
            Description = "Baseline board setup for engineering teams",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                MaxTaskdeckVersion = "2.0.0",
                RequiredFeatures = ["boards", "labels"]
            },
            Tags = ["starter", "engineering"],
            Labels =
            [
                new StarterPackLabelDto
                {
                    Name = "priority-high",
                    Color = "#E85D5D",
                    Description = "High urgency"
                }
            ],
            Columns =
            [
                new StarterPackColumnDto
                {
                    Name = "Backlog",
                    Position = 0
                },
                new StarterPackColumnDto
                {
                    Name = "Done",
                    Position = 1
                }
            ],
            Templates =
            [
                new StarterPackCardTemplateDto
                {
                    TemplateId = "bug-report",
                    Title = "Bug Report",
                    Description = "Template for bug triage",
                    Checklist = ["Reproduction steps", "Expected behavior", "Actual behavior"]
                }
            ],
            SeedCards =
            [
                new StarterPackSeedCardDto
                {
                    Title = "Set up sprint board",
                    Description = "Create initial sprint lanes",
                    ColumnName = "Backlog",
                    TemplateId = "bug-report",
                    Labels = ["priority-high"]
                }
            ]
        };
    }

    private static StarterPackManifestDto BuildLabelOnlyManifest()
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "common-labels-core",
            DisplayName = "Common Labels Core",
            Description = "Reusable label taxonomy for existing boards",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                RequiredFeatures = ["boards", "labels"]
            },
            Tags = ["starter", "labels"],
            Labels =
            [
                new StarterPackLabelDto
                {
                    Name = "priority-high",
                    Color = "#E85D5D",
                    Description = "High urgency"
                }
            ],
            Columns = [],
            Templates = [],
            SeedCards = []
        };
    }

    private static StarterPackManifestDto BuildPositionConflictManifest()
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "conflict-pack",
            DisplayName = "Conflict Pack",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                RequiredFeatures = ["boards"]
            },
            Tags = ["starter"],
            Labels = [],
            Columns =
            [
                new StarterPackColumnDto
                {
                    Name = "Backlog",
                    Position = 0
                }
            ],
            Templates = [],
            SeedCards = []
        };
    }

    private static StarterPackManifestDto BuildEmptyManifest()
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "empty-pack",
            DisplayName = "Empty Pack",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                RequiredFeatures = ["boards"]
            },
            Tags = ["starter"],
            Labels = [],
            Columns = [],
            Templates = [],
            SeedCards = []
        };
    }

    private static StarterPackManifestDto BuildColumnOnlyManifest()
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "column-only-pack",
            DisplayName = "Column Only Pack",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                RequiredFeatures = ["boards"]
            },
            Tags = ["starter", "columns"],
            Labels = [],
            Columns =
            [
                new StarterPackColumnDto
                {
                    Name = "Backlog",
                    Position = 0
                },
                new StarterPackColumnDto
                {
                    Name = "Done",
                    Position = 1
                }
            ],
            Templates = [],
            SeedCards = []
        };
    }

    private static StarterPackManifestDto BuildTemplateOnlyManifest()
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "template-only-pack",
            DisplayName = "Template Only Pack",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                RequiredFeatures = ["boards"]
            },
            Tags = ["starter", "templates"],
            Labels = [],
            Columns = [],
            Templates =
            [
                new StarterPackCardTemplateDto
                {
                    TemplateId = "bug-report",
                    Title = "Bug Report",
                    Description = "Template for bug triage",
                    Checklist = ["Reproduction steps"]
                }
            ],
            SeedCards = []
        };
    }

    private static StarterPackManifestDto BuildUnresolvableSeedCardManifest()
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "seed-card-unresolvable-warning-pack",
            DisplayName = "Seed Card Unresolvable Warning Pack",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                RequiredFeatures = ["boards"]
            },
            Tags = ["starter"],
            Labels = [],
            Columns =
            [
                new StarterPackColumnDto
                {
                    Name = "Backlog",
                    Position = 0
                }
            ],
            Templates = [],
            SeedCards =
            [
                new StarterPackSeedCardDto
                {
                    Title = "Investigate intake",
                    Description = "Investigate unresolvable column references",
                    ColumnName = "Backlog",
                    Labels = []
                }
            ]
        };
    }

}
