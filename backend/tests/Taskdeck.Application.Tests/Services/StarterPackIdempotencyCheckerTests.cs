using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class StarterPackIdempotencyCheckerTests
{
    private readonly StarterPackIdempotencyChecker _checker = new();

    [Fact]
    public void Check_ShouldPlanSeedCard_WhenColumnAndLabelsResolve()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var manifest = BuildManifestWithSeedCards(
            [new StarterPackSeedCardDto { Title = "Task 1", ColumnName = "Backlog", Labels = ["bug"] }]);

        var report = BuildReport(
            resolvableColumns: ["Backlog"],
            resolvableLabels: ["bug"]);

        var planned = _checker.Check(board, manifest, report);

        planned.Should().HaveCount(1);
        planned[0].SeedCard.Title.Should().Be("Task 1");
        report.Actions.Should().Contain(a => a.EntityType == "seedCard" && a.Operation == "create");
    }

    [Fact]
    public void Check_ShouldSkipSeedCard_WhenColumnCannotBeResolved()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var manifest = BuildManifestWithSeedCards(
            [new StarterPackSeedCardDto { Title = "Task 1", ColumnName = "Missing", Labels = [] }]);

        var report = BuildReport(
            resolvableColumns: ["Backlog"],
            resolvableLabels: []);

        var planned = _checker.Check(board, manifest, report);

        planned.Should().BeEmpty();
        report.Conflicts.Should().Contain(c => c.Code == "SeedCardColumnConflict");
        report.Actions.Should().Contain(a => a.EntityType == "seedCard" && a.Operation == "skip");
    }

    [Fact]
    public void Check_ShouldSkipSeedCard_WhenLabelCannotBeResolved()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var manifest = BuildManifestWithSeedCards(
            [new StarterPackSeedCardDto { Title = "Task 1", ColumnName = "Backlog", Labels = ["missing-label"] }]);

        var report = BuildReport(
            resolvableColumns: ["Backlog"],
            resolvableLabels: ["bug"]);

        var planned = _checker.Check(board, manifest, report);

        planned.Should().BeEmpty();
        report.Conflicts.Should().Contain(c => c.Code == "SeedCardLabelConflict");
    }

    [Fact]
    public void Check_ShouldSkipSeedCard_WhenAlreadyExistsOnBoard()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var column = TestDataBuilder.CreateColumn(board.Id, "Backlog", position: 0);
        board.AddColumn(column);
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Existing Task", position: 0);
        column.AddCard(card);
        board.AddCard(card);

        var manifest = BuildManifestWithSeedCards(
            [new StarterPackSeedCardDto { Title = "Existing Task", ColumnName = "Backlog", Labels = [] }]);

        var existingColumnsByName = new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backlog"] = column
        };
        var report = BuildReport(
            resolvableColumns: ["Backlog"],
            resolvableLabels: [],
            existingColumnsByName: existingColumnsByName);

        var planned = _checker.Check(board, manifest, report);

        planned.Should().BeEmpty();
        report.Conflicts.Should().Contain(c => c.Code == "SeedCardAlreadyExistsConflict");
    }

    [Fact]
    public void Check_ShouldSkipDuplicateSeedCard_InSameManifest()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var manifest = BuildManifestWithSeedCards(
        [
            new StarterPackSeedCardDto { Title = "Task", ColumnName = "Backlog", Labels = [] },
            new StarterPackSeedCardDto { Title = "Task", ColumnName = "Backlog", Labels = [] }
        ]);

        var report = BuildReport(
            resolvableColumns: ["Backlog"],
            resolvableLabels: []);

        var planned = _checker.Check(board, manifest, report);

        planned.Should().HaveCount(1);
        report.Conflicts.Should().Contain(c => c.Code == "SeedCardDuplicateInManifestConflict");
    }

    [Fact]
    public void Check_ShouldDeduplicateSeedCardLabels()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var manifest = BuildManifestWithSeedCards(
            [new StarterPackSeedCardDto { Title = "Task", ColumnName = "Backlog", Labels = ["bug", "bug", "feature"] }]);

        var report = BuildReport(
            resolvableColumns: ["Backlog"],
            resolvableLabels: ["bug", "feature"]);

        var planned = _checker.Check(board, manifest, report);

        planned.Should().HaveCount(1);
        planned[0].LabelNames.Should().BeEquivalentTo(["bug", "feature"]);
    }

    [Fact]
    public void Check_ShouldReportWarningConflicts_NotBlocking()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var manifest = BuildManifestWithSeedCards(
            [new StarterPackSeedCardDto { Title = "Task", ColumnName = "Missing", Labels = [] }]);

        var report = BuildReport(
            resolvableColumns: ["Backlog"],
            resolvableLabels: []);

        _checker.Check(board, manifest, report);

        report.Conflicts.Should().OnlyContain(c => c.Severity == StarterPackConflictSeverity.Warning);
    }

    private static StarterPackManifestDto BuildManifestWithSeedCards(List<StarterPackSeedCardDto> seedCards)
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "test-pack",
            DisplayName = "Test",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                RequiredFeatures = []
            },
            Tags = [],
            Labels = [],
            Columns = [],
            Templates = [],
            SeedCards = seedCards
        };
    }

    private static StarterPackConflictReport BuildReport(
        IEnumerable<string> resolvableColumns,
        IEnumerable<string> resolvableLabels,
        Dictionary<string, Column>? existingColumnsByName = null,
        Dictionary<string, Label>? existingLabelsByName = null)
    {
        return new StarterPackConflictReport(
            Actions: new List<StarterPackApplyActionDto>(),
            Conflicts: new List<StarterPackApplyConflictDto>(),
            PlannedLabels: new List<StarterPackLabelDto>(),
            PlannedLabelNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            PlannedColumns: new List<StarterPackColumnDto>(),
            PlannedColumnsByName: new Dictionary<string, StarterPackColumnDto>(StringComparer.OrdinalIgnoreCase),
            ExistingLabelsByName: existingLabelsByName ?? new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase),
            ExistingColumnsByName: existingColumnsByName ?? new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase),
            ResolvableColumnNames: new HashSet<string>(resolvableColumns, StringComparer.OrdinalIgnoreCase),
            ResolvableLabelNames: new HashSet<string>(resolvableLabels, StringComparer.OrdinalIgnoreCase));
    }
}
