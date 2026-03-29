using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class StarterPackConflictDetectorTests
{
    private readonly StarterPackConflictDetector _detector = new();

    [Fact]
    public void DetectConflicts_ShouldCreateLabelsAndColumns_WhenBoardIsEmpty()
    {
        var board = TestDataBuilder.CreateBoard("Empty Board");
        var manifest = BuildManifest(
            labels: [new StarterPackLabelDto { Name = "bug", Color = "#FF0000" }],
            columns: [new StarterPackColumnDto { Name = "Backlog", Position = 0 }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.Actions.Should().Contain(a => a.EntityType == "label" && a.Operation == "create" && a.Key == "bug");
        report.Actions.Should().Contain(a => a.EntityType == "column" && a.Operation == "create" && a.Key == "Backlog");
        report.Conflicts.Should().BeEmpty();
        report.PlannedLabels.Should().HaveCount(1);
        report.PlannedColumns.Should().HaveCount(1);
    }

    [Fact]
    public void DetectConflicts_ShouldSkipLabel_WhenExistingLabelMatchesColor()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var label = TestDataBuilder.CreateLabel(board.Id, "bug", "#FF0000");
        board.AddLabel(label);

        var manifest = BuildManifest(
            labels: [new StarterPackLabelDto { Name = "bug", Color = "#FF0000" }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.Actions.Should().Contain(a => a.EntityType == "label" && a.Operation == "skip");
        report.PlannedLabels.Should().BeEmpty();
        report.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void DetectConflicts_ShouldReportConflict_WhenExistingLabelHasDifferentColor()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var label = TestDataBuilder.CreateLabel(board.Id, "bug", "#00FF00");
        board.AddLabel(label);

        var manifest = BuildManifest(
            labels: [new StarterPackLabelDto { Name = "bug", Color = "#FF0000" }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.Conflicts.Should().Contain(c => c.Code == "LabelColorConflict");
    }

    [Fact]
    public void DetectConflicts_ShouldSkipColumn_WhenExistingColumnMatchesDefinition()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var column = TestDataBuilder.CreateColumn(board.Id, "Backlog", position: 0);
        board.AddColumn(column);

        var manifest = BuildManifest(
            columns: [new StarterPackColumnDto { Name = "Backlog", Position = 0 }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.Actions.Should().Contain(a => a.EntityType == "column" && a.Operation == "skip");
        report.PlannedColumns.Should().BeEmpty();
    }

    [Fact]
    public void DetectConflicts_ShouldReportConflict_WhenExistingColumnHasDifferentDefinition()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var column = TestDataBuilder.CreateColumn(board.Id, "Backlog", position: 0, wipLimit: 3);
        board.AddColumn(column);

        var manifest = BuildManifest(
            columns: [new StarterPackColumnDto { Name = "Backlog", Position = 0, WipLimit = 5 }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.Conflicts.Should().Contain(c => c.Code == "ColumnDefinitionConflict");
    }

    [Fact]
    public void DetectConflicts_ShouldReportConflict_WhenColumnPositionIsOccupied()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        var existingColumn = TestDataBuilder.CreateColumn(board.Id, "Existing", position: 0);
        board.AddColumn(existingColumn);

        var manifest = BuildManifest(
            columns: [new StarterPackColumnDto { Name = "New Column", Position = 0 }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.Conflicts.Should().Contain(c => c.Code == "ColumnPositionConflict");
    }

    [Fact]
    public void DetectConflicts_ShouldReportConflict_WhenBoardHasDuplicateLabelNames()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        board.AddLabel(TestDataBuilder.CreateLabel(board.Id, "bug", "#FF0000"));
        board.AddLabel(TestDataBuilder.CreateLabel(board.Id, "bug", "#00FF00"));

        var manifest = BuildManifest(
            labels: [new StarterPackLabelDto { Name = "bug", Color = "#FF0000" }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.Conflicts.Should().Contain(c => c.Code == "ExistingLabelNameConflict");
    }

    [Fact]
    public void DetectConflicts_ShouldReportConflict_WhenBoardHasDuplicateColumnNames()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        board.AddColumn(TestDataBuilder.CreateColumn(board.Id, "Backlog", position: 0));
        board.AddColumn(TestDataBuilder.CreateColumn(board.Id, "Backlog", position: 1));

        var manifest = BuildManifest(
            columns: [new StarterPackColumnDto { Name = "Backlog", Position = 0 }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.Conflicts.Should().Contain(c => c.Code == "ExistingColumnNameConflict");
    }

    [Fact]
    public void DetectConflicts_ShouldPopulateResolvableNames()
    {
        var board = TestDataBuilder.CreateBoard("Board");
        board.AddLabel(TestDataBuilder.CreateLabel(board.Id, "existing-label", "#111111"));

        var manifest = BuildManifest(
            labels: [new StarterPackLabelDto { Name = "new-label", Color = "#222222" }],
            columns: [new StarterPackColumnDto { Name = "New Column", Position = 0 }]);

        var report = _detector.DetectConflicts(board, manifest);

        report.ResolvableLabelNames.Should().Contain("existing-label");
        report.ResolvableLabelNames.Should().Contain("new-label");
        report.ResolvableColumnNames.Should().Contain("New Column");
    }

    [Fact]
    public void DescribeColumn_ShouldFormatCorrectly()
    {
        StarterPackConflictDetector.DescribeColumn(2, 5).Should().Be("position=2, wipLimit=5");
        StarterPackConflictDetector.DescribeColumn(0, null).Should().Be("position=0, wipLimit=null");
    }

    private static StarterPackManifestDto BuildManifest(
        List<StarterPackLabelDto>? labels = null,
        List<StarterPackColumnDto>? columns = null,
        List<StarterPackSeedCardDto>? seedCards = null)
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
            Labels = labels ?? [],
            Columns = columns ?? [],
            Templates = [],
            SeedCards = seedCards ?? []
        };
    }
}
