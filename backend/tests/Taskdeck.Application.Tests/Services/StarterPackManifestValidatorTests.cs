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

