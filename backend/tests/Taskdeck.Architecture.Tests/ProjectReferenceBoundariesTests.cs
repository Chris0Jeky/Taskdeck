using System.Xml.Linq;
using Xunit;

namespace Taskdeck.Architecture.Tests;

public class ProjectReferenceBoundariesTests
{
    [Theory]
    [InlineData("src/Taskdeck.Domain/Taskdeck.Domain.csproj", "Taskdeck.Infrastructure.csproj")]
    [InlineData("src/Taskdeck.Domain/Taskdeck.Domain.csproj", "Taskdeck.Api.csproj")]
    [InlineData("src/Taskdeck.Application/Taskdeck.Application.csproj", "Taskdeck.Api.csproj")]
    [InlineData("src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj", "Taskdeck.Api.csproj")]
    public void ProjectReferences_ShouldNotContainForbiddenDependencies(string projectPath, string forbiddenProjectFileName)
    {
        var references = ReadProjectReferenceFileNames(projectPath);

        Assert.DoesNotContain(
            references,
            reference => string.Equals(reference, forbiddenProjectFileName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ReadProjectReferenceFileNames(string projectPath)
    {
        var fullProjectPath = ArchitectureTestPaths.GetBackendPath(projectPath);

        var projectDocument = XDocument.Load(fullProjectPath);
        return projectDocument
            .Descendants("ProjectReference")
            .Select(node => node.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileName(value!))
            .ToList();
    }
}
