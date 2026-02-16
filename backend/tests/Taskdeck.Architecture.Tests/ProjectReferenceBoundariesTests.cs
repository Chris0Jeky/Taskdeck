using System.Xml.Linq;
using Xunit;

namespace Taskdeck.Architecture.Tests;

public class ProjectReferenceBoundariesTests
{
    private static readonly string BackendRoot = ResolveBackendRoot();

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
        var fullProjectPath = Path.Combine(
            BackendRoot,
            projectPath.Replace('/', Path.DirectorySeparatorChar));

        var projectDocument = XDocument.Load(fullProjectPath);
        return projectDocument
            .Descendants("ProjectReference")
            .Select(node => node.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileName(value!))
            .ToList();
    }

    private static string ResolveBackendRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "backend", "Taskdeck.sln");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate backend/Taskdeck.sln from test execution directory.");
    }
}
