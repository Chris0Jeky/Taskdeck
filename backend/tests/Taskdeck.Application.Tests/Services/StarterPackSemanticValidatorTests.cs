using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class StarterPackSemanticValidatorTests
{
    private readonly StarterPackSemanticValidator _validator = new();

    [Fact]
    public void Validate_ShouldNotAddErrors_WhenAllReferencesAreValid()
    {
        var manifest = BuildManifestWithSeedCard("Backlog", "bug-report", ["priority-high"]);
        var schemaOutput = new StarterPackSchemaValidationOutput(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "priority-high" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Backlog" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bug-report" });
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, schemaOutput, errors);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldAddError_WhenSeedCardReferencesUnknownColumn()
    {
        var manifest = BuildManifestWithSeedCard("NonExistent", null, []);
        var schemaOutput = new StarterPackSchemaValidationOutput(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Backlog" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, schemaOutput, errors);

        errors.Should().Contain(e =>
            e.Path == "$.seedCards[0].columnName" &&
            e.Message.Contains("unknown column", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldAddError_WhenSeedCardReferencesUnknownLabel()
    {
        var manifest = BuildManifestWithSeedCard("Backlog", null, ["missing-label"]);
        var schemaOutput = new StarterPackSchemaValidationOutput(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "priority-high" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Backlog" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, schemaOutput, errors);

        errors.Should().Contain(e =>
            e.Path == "$.seedCards[0].labels[0]" &&
            e.Message.Contains("unknown label", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldAddError_WhenSeedCardReferencesUnknownTemplate()
    {
        var manifest = BuildManifestWithSeedCard("Backlog", "missing-template", []);
        var schemaOutput = new StarterPackSchemaValidationOutput(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Backlog" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bug-report" });
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, schemaOutput, errors);

        errors.Should().Contain(e =>
            e.Path == "$.seedCards[0].templateId" &&
            e.Message.Contains("unknown template", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldAddError_WhenSeedCardTitleIsMissing()
    {
        var manifest = BuildManifestWithSeedCard("Backlog", null, []);
        manifest.SeedCards[0].Title = "";
        var schemaOutput = new StarterPackSchemaValidationOutput(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Backlog" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, schemaOutput, errors);

        errors.Should().Contain(e => e.Path == "$.seedCards[0].title");
    }

    [Fact]
    public void Validate_ShouldAddError_WhenSeedCardColumnNameIsMissing()
    {
        var manifest = BuildManifestWithSeedCard("", null, []);
        var schemaOutput = new StarterPackSchemaValidationOutput(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, schemaOutput, errors);

        errors.Should().Contain(e => e.Path == "$.seedCards[0].columnName");
    }

    [Fact]
    public void Validate_ShouldAddError_WhenSeedCardLabelIsEmpty()
    {
        var manifest = BuildManifestWithSeedCard("Backlog", null, [""]);
        var schemaOutput = new StarterPackSchemaValidationOutput(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Backlog" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, schemaOutput, errors);

        errors.Should().Contain(e =>
            e.Path == "$.seedCards[0].labels[0]" &&
            e.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    private static StarterPackManifestDto BuildManifestWithSeedCard(
        string columnName, string? templateId, List<string> labels)
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
            SeedCards =
            [
                new StarterPackSeedCardDto
                {
                    Title = "Test Card",
                    ColumnName = columnName,
                    TemplateId = templateId,
                    Labels = labels
                }
            ]
        };
    }
}
