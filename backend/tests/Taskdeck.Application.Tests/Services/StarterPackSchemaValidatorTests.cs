using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class StarterPackSchemaValidatorTests
{
    private readonly StarterPackSchemaValidator _validator = new();

    [Fact]
    public void Validate_ShouldReturnKnownNames_ForValidManifest()
    {
        var manifest = BuildValidManifest();
        var errors = new List<StarterPackManifestValidationError>();

        var output = _validator.Validate(manifest, errors);

        errors.Should().BeEmpty();
        output.KnownLabelNames.Should().Contain("priority-high");
        output.KnownColumnNames.Should().Contain("Backlog");
        output.KnownTemplateIds.Should().Contain("bug-report");
    }

    [Fact]
    public void Validate_ShouldReportError_WhenSchemaVersionIsWrong()
    {
        var manifest = BuildValidManifest();
        manifest.SchemaVersion = "99.0";
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.schemaVersion");
    }

    [Fact]
    public void Validate_ShouldReportError_WhenPackIdIsNotSlug()
    {
        var manifest = BuildValidManifest();
        manifest.PackId = "Not A Slug";
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.packId");
    }

    [Fact]
    public void Validate_ShouldReportError_WhenDisplayNameIsEmpty()
    {
        var manifest = BuildValidManifest();
        manifest.DisplayName = "";
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.displayName");
    }

    [Fact]
    public void Validate_ShouldReportError_WhenTagIsDuplicate()
    {
        var manifest = BuildValidManifest();
        manifest.Tags = ["starter", "starter"];
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.tags[1]" && e.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReportError_WhenCompatibilityIsMissing()
    {
        var manifest = BuildValidManifest();
        manifest.Compatibility = null!;
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.compatibility");
    }

    [Fact]
    public void Validate_ShouldReportError_WhenLabelColorIsNotHex()
    {
        var manifest = BuildValidManifest();
        manifest.Labels[0].Color = "red";
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.labels[0].color");
    }

    [Fact]
    public void Validate_ShouldReportError_WhenColumnPositionsAreNotContiguous()
    {
        var manifest = BuildValidManifest();
        manifest.Columns =
        [
            new StarterPackColumnDto { Name = "A", Position = 0 },
            new StarterPackColumnDto { Name = "B", Position = 5 }
        ];
        manifest.SeedCards[0].ColumnName = "A";
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.columns" && e.Message.Contains("contiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReportError_WhenWipLimitIsZero()
    {
        var manifest = BuildValidManifest();
        manifest.Columns[1].WipLimit = 0;
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.columns[1].wipLimit");
    }

    [Fact]
    public void Validate_ShouldReportError_WhenNoBoardArtifacts()
    {
        var manifest = BuildValidManifest();
        manifest.Labels = [];
        manifest.Columns = [];
        manifest.Templates = [];
        manifest.SeedCards = [];
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$" && e.Message.Contains("at least one", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReportError_WhenTemplateIdIsDuplicate()
    {
        var manifest = BuildValidManifest();
        manifest.Templates =
        [
            new StarterPackCardTemplateDto { TemplateId = "tmpl", Title = "A", Checklist = ["x"] },
            new StarterPackCardTemplateDto { TemplateId = "tmpl", Title = "B", Checklist = ["y"] }
        ];
        manifest.SeedCards[0].TemplateId = null;
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.templates[1].templateId" && e.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReportError_WhenCollectionIsNull()
    {
        var manifest = BuildValidManifest();
        manifest.Tags = null!;
        var errors = new List<StarterPackManifestValidationError>();

        _validator.Validate(manifest, errors);

        errors.Should().Contain(e => e.Path == "$.tags" && e.Message.Contains("must be an array", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NormalizeCollection_ShouldReturnEmptyList_WhenNull()
    {
        var errors = new List<StarterPackManifestValidationError>();

        var result = StarterPackSchemaValidator.NormalizeCollection<string>(null, "$.test", "Test", errors);

        result.Should().BeEmpty();
        errors.Should().ContainSingle(e => e.Path == "$.test");
    }

    [Fact]
    public void NormalizeCollection_ShouldReturnSameList_WhenNotNull()
    {
        var errors = new List<StarterPackManifestValidationError>();
        var input = new List<string> { "a", "b" };

        var result = StarterPackSchemaValidator.NormalizeCollection(input, "$.test", "Test", errors);

        result.Should().BeSameAs(input);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void IsSlug_ShouldReturnTrue_ForValidSlug()
    {
        StarterPackSchemaValidator.IsSlug("my-valid-slug").Should().BeTrue();
    }

    [Fact]
    public void IsSlug_ShouldReturnFalse_ForInvalidSlug()
    {
        StarterPackSchemaValidator.IsSlug("Not A Slug").Should().BeFalse();
        StarterPackSchemaValidator.IsSlug("").Should().BeFalse();
        StarterPackSchemaValidator.IsSlug(null).Should().BeFalse();
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
                new StarterPackLabelDto { Name = "priority-high", Color = "#E85D5D" }
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
