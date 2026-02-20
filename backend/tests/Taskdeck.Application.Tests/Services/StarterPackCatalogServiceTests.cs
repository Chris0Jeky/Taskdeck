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
        catalog.Count(entry => entry.Category == StarterPackCatalogCategories.BoardBlueprint).Should().Be(3);
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
}
