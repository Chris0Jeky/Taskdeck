using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class StarterPackManifestValidatorTests
{
    private readonly StarterPackManifestValidator _validator = new();

    [Fact]
    public void ValidateJson_ShouldReturnValidResult_ForCanonicalManifest()
    {
        var result = _validator.ValidateJson(
            """
            {
              "schemaVersion": "1.0",
              "packId": "engineering-onboarding",
              "displayName": "Engineering Onboarding",
              "description": "Baseline board setup for engineering teams",
              "compatibility": {
                "minTaskdeckVersion": "1.0.0",
                "maxTaskdeckVersion": "2.0.0",
                "requiredFeatures": ["boards", "labels"]
              },
              "tags": ["starter", "engineering"],
              "labels": [
                { "name": "priority-high", "color": "#E85D5D", "description": "High urgency" },
                { "name": "blocked", "color": "#4A5568", "description": "Waiting on dependency" }
              ],
              "columns": [
                { "name": "Backlog", "position": 0 },
                { "name": "In Progress", "position": 1, "wipLimit": 5 },
                { "name": "Done", "position": 2 }
              ],
              "templates": [
                {
                  "templateId": "bug-report",
                  "title": "Bug Report",
                  "description": "Template for bug triage",
                  "checklist": ["Reproduction steps", "Expected behavior", "Actual behavior"]
                }
              ],
              "seedCards": [
                {
                  "title": "Set up sprint board",
                  "description": "Create initial sprint lanes",
                  "columnName": "Backlog",
                  "templateId": "bug-report",
                  "labels": ["priority-high"]
                }
              ]
            }
            """);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Manifest.Should().NotBeNull();
        result.Manifest!.PackId.Should().Be("engineering-onboarding");
        result.Manifest.Columns.Should().HaveCount(3);
    }

    [Fact]
    public void ValidateJson_ShouldReturnError_WhenJsonIsMalformed()
    {
        var result = _validator.ValidateJson("{ this is not valid json }");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Path == "$");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenSchemaVersionUnsupported()
    {
        var manifest = BuildValidManifest();
        manifest.SchemaVersion = "2.0";

        var result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Path == "$.schemaVersion");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenColumnPositionsAreNotContiguous()
    {
        var manifest = BuildValidManifest();
        manifest.Columns =
        [
            new StarterPackColumnDto { Name = "Backlog", Position = 0 },
            new StarterPackColumnDto { Name = "Done", Position = 2 }
        ];

        var result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Path == "$.columns" &&
            error.Message.Contains("contiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenSeedCardReferencesUnknownColumn()
    {
        var manifest = BuildValidManifest();
        manifest.SeedCards =
        [
            new StarterPackSeedCardDto
            {
                Title = "Seed",
                ColumnName = "NotAColumn",
                Labels = ["priority-high"]
            }
        ];

        var result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Path == "$.seedCards[0].columnName");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenSeedCardReferencesUnknownLabel()
    {
        var manifest = BuildValidManifest();
        manifest.SeedCards =
        [
            new StarterPackSeedCardDto
            {
                Title = "Seed",
                ColumnName = "Backlog",
                Labels = ["unknown-label"]
            }
        ];

        var result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Path == "$.seedCards[0].labels[0]");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenCompatibilityRangeIsInvalid()
    {
        var manifest = BuildValidManifest();
        manifest.Compatibility = new StarterPackCompatibilityDto
        {
            MinTaskdeckVersion = "2.0.0",
            MaxTaskdeckVersion = "1.0.0"
        };

        var result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Path == "$.compatibility.maxTaskdeckVersion" &&
            error.Message.Contains("greater than or equal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenTemplateChecklistContainsEmptyItem()
    {
        var manifest = BuildValidManifest();
        manifest.Templates =
        [
            new StarterPackCardTemplateDto
            {
                TemplateId = "bug-report",
                Title = "Bug Report",
                Checklist = ["Valid item", " "]
            }
        ];

        var result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Path == "$.templates[0].checklist[1]");
    }

    [Fact]
    public void ValidateJson_ShouldReturnErrors_WhenCollectionsAreExplicitlyNull()
    {
        var result = _validator.ValidateJson(
            """
            {
              "schemaVersion": "1.0",
              "packId": "engineering-onboarding",
              "displayName": "Engineering Onboarding",
              "compatibility": {
                "minTaskdeckVersion": "1.0.0",
                "maxTaskdeckVersion": "2.0.0",
                "requiredFeatures": null
              },
              "tags": null,
              "labels": null,
              "columns": [
                { "name": "Backlog", "position": 0 }
              ],
              "templates": [
                {
                  "templateId": "bug-report",
                  "title": "Bug Report",
                  "checklist": null
                }
              ],
              "seedCards": [
                {
                  "title": "Set up sprint board",
                  "columnName": "Backlog",
                  "templateId": "bug-report",
                  "labels": null
                }
              ]
            }
            """);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Path == "$.tags" &&
            error.Message.Contains("must be an array", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error =>
            error.Path == "$.compatibility.requiredFeatures" &&
            error.Message.Contains("must be an array", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error =>
            error.Path == "$.labels" &&
            error.Message.Contains("must be an array", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error =>
            error.Path == "$.templates[0].checklist" &&
            error.Message.Contains("must be an array", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error =>
            error.Path == "$.seedCards[0].labels" &&
            error.Message.Contains("must be an array", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenTagsContainDuplicates()
    {
        var manifest = BuildValidManifest();
        manifest.Tags = ["starter", "starter"];

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.tags[1]", "duplicate tag");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenRequiredFeaturesContainDuplicates()
    {
        var manifest = BuildValidManifest();
        manifest.Compatibility.RequiredFeatures = ["boards", "boards"];

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.compatibility.requiredFeatures[1]", "duplicate required feature");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenLabelNamesContainDuplicates()
    {
        var manifest = BuildValidManifest();
        manifest.Labels =
        [
            new StarterPackLabelDto { Name = "priority-high", Color = "#E85D5D" },
            new StarterPackLabelDto { Name = "priority-high", Color = "#4A5568" }
        ];
        manifest.SeedCards[0].Labels = [];

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.labels[1].name", "duplicate label name");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenColumnNamesContainDuplicates()
    {
        var manifest = BuildValidManifest();
        manifest.Columns[1].Name = "Backlog";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.columns[1].name", "duplicate column name");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenColumnPositionsContainDuplicates()
    {
        var manifest = BuildValidManifest();
        manifest.Columns[1].Position = 0;

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.columns[1].position", "duplicate column position");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenTemplateIdsContainDuplicates()
    {
        var manifest = BuildValidManifest();
        manifest.Templates =
        [
            new StarterPackCardTemplateDto
            {
                TemplateId = "bug-report",
                Title = "Bug Report",
                Checklist = ["Repro steps"]
            },
            new StarterPackCardTemplateDto
            {
                TemplateId = "bug-report",
                Title = "Incident Report",
                Checklist = ["Capture timeline"]
            }
        ];

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.templates[1].templateId", "duplicate template id");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenDisplayNameIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.DisplayName = " ";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.displayName", "required");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenMinimumTaskdeckVersionIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.Compatibility.MinTaskdeckVersion = "";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.compatibility.minTaskdeckVersion", "strict semver");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenLabelNameIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.Labels[0].Name = "";
        manifest.SeedCards[0].Labels = [];

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.labels[0].name", "required");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenLabelColorIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.Labels[0].Color = "";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.labels[0].color", "hex rgb");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenColumnNameIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.Columns[1].Name = "";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.columns[1].name", "required");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenTemplateIdIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.Templates[0].TemplateId = "";
        manifest.SeedCards[0].TemplateId = null;

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.templates[0].templateId", "kebab-case");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenTemplateTitleIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.Templates[0].Title = "";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.templates[0].title", "required");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenSeedCardTitleIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.SeedCards[0].Title = "";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.seedCards[0].title", "required");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenSeedCardColumnNameIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.SeedCards[0].ColumnName = "";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.seedCards[0].columnName", "required");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenPackIdIsNotKebabCase()
    {
        var manifest = BuildValidManifest();
        manifest.PackId = "Engineering_Onboarding";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.packId", "kebab-case");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenTagIsNotKebabCase()
    {
        var manifest = BuildValidManifest();
        manifest.Tags = ["not kebab case"];

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.tags[0]", "kebab-case");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenRequiredFeatureIsNotKebabCase()
    {
        var manifest = BuildValidManifest();
        manifest.Compatibility.RequiredFeatures = ["boards", "FeatureFlag"];

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.compatibility.requiredFeatures[1]", "kebab-case");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenTemplateIdIsNotKebabCase()
    {
        var manifest = BuildValidManifest();
        manifest.Templates[0].TemplateId = "Bug_Report";
        manifest.SeedCards[0].TemplateId = null;

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.templates[0].templateId", "kebab-case");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenLabelColorIsNotHexRgb()
    {
        var manifest = BuildValidManifest();
        manifest.Labels[0].Color = "#12345";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.labels[0].color", "hex rgb");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenMinimumTaskdeckVersionIsNotStrictSemVer()
    {
        var manifest = BuildValidManifest();
        manifest.Compatibility.MinTaskdeckVersion = "1.0";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.compatibility.minTaskdeckVersion", "strict semver");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenMaximumTaskdeckVersionIsNotStrictSemVer()
    {
        var manifest = BuildValidManifest();
        manifest.Compatibility.MaxTaskdeckVersion = "2.0";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.compatibility.maxTaskdeckVersion", "strict semver");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenColumnsAreEmpty()
    {
        var manifest = BuildValidManifest();
        manifest.Columns = [];
        manifest.SeedCards = [];

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.columns", "at least one column is required");
    }

    [Fact]
    public void Validate_ShouldReturnError_WhenSeedCardReferencesUnknownTemplate()
    {
        var manifest = BuildValidManifest();
        manifest.SeedCards[0].TemplateId = "unknown-template";

        var result = _validator.Validate(manifest);

        ShouldContainError(result, "$.seedCards[0].templateId", "unknown template");
    }

    private static void ShouldContainError(
        StarterPackManifestValidationResult result,
        string path,
        string messageFragment)
    {
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Path == path &&
            error.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase));
    }

    private static StarterPackManifestDto BuildValidManifest()
    {
        return new StarterPackManifestDto
        {
            SchemaVersion = "1.0",
            PackId = "engineering-onboarding",
            DisplayName = "Engineering Onboarding",
            Compatibility = new StarterPackCompatibilityDto
            {
                MinTaskdeckVersion = "1.0.0",
                MaxTaskdeckVersion = "2.0.0",
                RequiredFeatures = ["boards"]
            },
            Tags = ["starter"],
            Labels =
            [
                new StarterPackLabelDto
                {
                    Name = "priority-high",
                    Color = "#E85D5D"
                }
            ],
            Columns =
            [
                new StarterPackColumnDto { Name = "Backlog", Position = 0 },
                new StarterPackColumnDto { Name = "In Progress", Position = 1 },
                new StarterPackColumnDto { Name = "Done", Position = 2 }
            ],
            Templates =
            [
                new StarterPackCardTemplateDto
                {
                    TemplateId = "bug-report",
                    Title = "Bug Report",
                    Checklist = ["Repro steps"]
                }
            ],
            SeedCards =
            [
                new StarterPackSeedCardDto
                {
                    Title = "Set up board",
                    ColumnName = "Backlog",
                    TemplateId = "bug-report",
                    Labels = ["priority-high"]
                }
            ]
        };
    }
}

