using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class StarterPackCatalogServiceTests
{
    private readonly StarterPackManifestValidator _validator = new();

    [Fact]
    public void GetCatalog_ShouldReturnRequiredFirstPartyPackCoverage()
    {
        var service = new StarterPackCatalogService(_validator);

        var catalog = service.GetCatalog();

        catalog.Should().NotBeNullOrEmpty();
        catalog.Should().OnlyHaveUniqueItems(entry => entry.Id);
        catalog.Count(entry => entry.Category == StarterPackCatalogCategories.LabelPack).Should().BeGreaterThanOrEqualTo(1);
        catalog.Count(entry => entry.Category == StarterPackCatalogCategories.ColumnFlow).Should().BeGreaterThanOrEqualTo(1);
        catalog.Count(entry => entry.Category == StarterPackCatalogCategories.BoardBlueprint).Should().Be(4);
    }

    [Fact]
    public void GetCatalog_ShouldReturnOnlyValidSchemaV1Manifests()
    {
        var service = new StarterPackCatalogService(_validator);

        var catalog = service.GetCatalog();

        foreach (var entry in catalog)
        {
            entry.Manifest.SchemaVersion.Should().Be("1.0");
            entry.Id.Should().Be(entry.Manifest.PackId);

            var validation = _validator.Validate(entry.Manifest);
            validation.IsValid.Should().BeTrue($"manifest '{entry.Id}' should be valid");
            validation.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void GetCatalog_ShouldKeepCommonLabelsPackBoardCompatible()
    {
        var service = new StarterPackCatalogService(_validator);

        var catalog = service.GetCatalog();

        var labelsPack = catalog.Single(entry => entry.Id == "common-labels-core");
        labelsPack.Category.Should().Be(StarterPackCatalogCategories.LabelPack);
        labelsPack.Manifest.Columns.Should().BeEmpty();
        labelsPack.Manifest.SeedCards.Should().BeEmpty();
    }

    [Fact]
    public void GetCatalog_ShouldKeepContentCalendarBlueprintContractStable()
    {
        var service = new StarterPackCatalogService(_validator);

        var catalog = service.GetCatalog();

        var contentCalendarPack = catalog.Single(entry => entry.Id == "board-blueprint-content-calendar");
        contentCalendarPack.Manifest.Columns.Select(column => column.Name).Should().Equal(
            "Ideas",
            "Drafting",
            "Review",
            "Scheduled");
        contentCalendarPack.Manifest.Labels.Select(label => label.Name).Should().Equal(
            "needs-draft",
            "needs-review",
            "publish-week");
        var seedCard = contentCalendarPack.Manifest.SeedCards.Should().ContainSingle().Subject;
        seedCard.Title.Should().Be("Plan weekly editorial slate");
        seedCard.ColumnName.Should().Be("Ideas");
        seedCard.Labels.Should().Equal("publish-week");
    }

    [Fact]
    public void GetCatalog_ShouldKeepClientOnboardingBlueprintContractStable()
    {
        var service = new StarterPackCatalogService(_validator);

        var catalog = service.GetCatalog();

        var clientOnboardingPack = catalog.Single(entry => entry.Id == "board-blueprint-client-onboarding");
        clientOnboardingPack.Manifest.Columns.Select(column => column.Name).Should().Equal(
            "New Intake",
            "Waiting on Client",
            "Ready for Review",
            "In Progress",
            "Completed");
        clientOnboardingPack.Manifest.Labels.Select(label => label.Name).Should().Equal(
            "client-action",
            "internal-review",
            "waiting-on-client");
        clientOnboardingPack.Manifest.SeedCards.Should().HaveCount(2);
        clientOnboardingPack.Manifest.SeedCards.Select(card => card.Title).Should().Contain(
            "Review new onboarding intake",
            "Confirm onboarding owner and due date");
    }

    [Fact]
    public void GetCatalog_ShouldKeepJsonScenariosAlignedWithFirstPartyStarterPacks()
    {
        var service = new StarterPackCatalogService(_validator);
        var catalogById = service.GetCatalog()
            .ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var scenarioPath in Directory.GetFiles(GetJsonScenarioDirectory(), "*.json"))
        {
            if (string.Equals(Path.GetFileName(scenarioPath), "schema.v1.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(scenarioPath));
            var root = document.RootElement;
            var scenarioId = root.GetProperty("id").GetString() ?? Path.GetFileNameWithoutExtension(scenarioPath);
            var steps = root.GetProperty("steps").EnumerateArray().ToArray();

            var starterPackStep = steps.FirstOrDefault(step =>
                step.TryGetProperty("type", out var stepType) &&
                string.Equals(stepType.GetString(), "applyStarterPack", StringComparison.Ordinal));
            var starterPackStepIndex = Array.FindIndex(steps, step =>
                step.TryGetProperty("type", out var stepType) &&
                string.Equals(stepType.GetString(), "applyStarterPack", StringComparison.Ordinal));

            starterPackStep.ValueKind.Should().NotBe(
                JsonValueKind.Undefined,
                $"scenario '{scenarioId}' should apply a first-party starter pack before creating pack-dependent artifacts");
            starterPackStepIndex.Should().BeGreaterThanOrEqualTo(0);
            starterPackStep.TryGetProperty("starterPackId", out var starterPackIdProperty).Should().BeTrue();

            var starterPackId = starterPackIdProperty.GetString();
            starterPackId.Should().NotBeNullOrWhiteSpace();
            catalogById.Should().ContainKey(starterPackId!);

            var manifest = catalogById[starterPackId!].Manifest;
            var knownColumns = manifest.Columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var knownLabels = manifest.Labels.Select(label => label.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < steps.Length; index++)
            {
                var step = steps[index];
                var stepType = step.GetProperty("type").GetString();

                if (string.Equals(stepType, "createCard", StringComparison.Ordinal))
                {
                    index.Should().BeGreaterThan(
                        starterPackStepIndex,
                        $"scenario '{scenarioId}' should apply starter pack '{starterPackId}' before Step[{index}] creates pack-dependent cards");
                    AssertScenarioColumn(step, "column", knownColumns, scenarioId, starterPackId!, index);
                    AssertScenarioLabels(step, knownLabels, scenarioId, starterPackId!, index);
                    continue;
                }

                if (string.Equals(stepType, "moveCard", StringComparison.Ordinal))
                {
                    index.Should().BeGreaterThan(
                        starterPackStepIndex,
                        $"scenario '{scenarioId}' should apply starter pack '{starterPackId}' before Step[{index}] moves cards into pack-defined columns");
                    AssertScenarioColumn(step, "toColumn", knownColumns, scenarioId, starterPackId!, index);
                }
            }
        }
    }

    private static void AssertScenarioColumn(
        JsonElement step,
        string propertyName,
        HashSet<string> knownColumns,
        string scenarioId,
        string starterPackId,
        int stepIndex)
    {
        if (!step.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        var columnName = property.GetString();
        columnName.Should().NotBeNullOrWhiteSpace();
        knownColumns.Should().Contain(
            columnName!,
            $"scenario '{scenarioId}' Step[{stepIndex}] should only use columns defined by starter pack '{starterPackId}'");
    }

    private static void AssertScenarioLabels(
        JsonElement step,
        HashSet<string> knownLabels,
        string scenarioId,
        string starterPackId,
        int stepIndex)
    {
        if (!step.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var labelProperty in labels.EnumerateArray())
        {
            var labelName = labelProperty.GetString();
            labelName.Should().NotBeNullOrWhiteSpace();
            knownLabels.Should().Contain(
                labelName!,
                $"scenario '{scenarioId}' Step[{stepIndex}] should only use labels defined by starter pack '{starterPackId}'");
        }
    }

    private static string GetJsonScenarioDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "frontend", "taskdeck-web", "scripts", "scenarios-json");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate frontend/taskdeck-web/scripts/scenarios-json from the test runtime.");
    }
}
