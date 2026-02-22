using System.Text.RegularExpressions;
using Xunit;

namespace Taskdeck.Architecture.Tests;

public class ApiControllerBoundaryTests
{
    private static readonly Regex ControllerDeclarationRegex = new(
        @"public\s+(?:abstract\s+)?(?:partial\s+)?class\s+(?<name>[A-Za-z0-9_]+)\s*:\s*(?<base>[A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex AuthorizeAttributeRegex = new(
        @"\[Authorize(?:\s*\(.*?\))?\]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly HashSet<string> AllowedControllerBaseTypes = new(StringComparer.Ordinal)
    {
        "AuthController",
        "HealthController"
    };

    [Fact]
    public void Controllers_ShouldOnlyUseControllerBaseDirectly_WhenExplicitlyAllowed()
    {
        var violations = new List<string>();

        foreach (var controllerFile in GetControllerFiles())
        {
            var content = File.ReadAllText(controllerFile);
            var declaration = FindControllerDeclaration(controllerFile, content);

            if (!declaration.BaseType.Equals("ControllerBase", StringComparison.Ordinal))
            {
                continue;
            }

            if (AllowedControllerBaseTypes.Contains(declaration.ClassName))
            {
                continue;
            }

            violations.Add(
                $"{ArchitectureTestPaths.ToBackendRelativePath(controllerFile)} declares {declaration.ClassName} : ControllerBase.");
        }

        Assert.True(
            violations.Count == 0,
            $"Unexpected direct ControllerBase inheritance detected:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void ProtectedControllers_ShouldDeclareAuthorizeAttribute()
    {
        var violations = new List<string>();

        foreach (var controllerFile in GetControllerFiles())
        {
            var content = File.ReadAllText(controllerFile);
            var declaration = FindControllerDeclaration(controllerFile, content);

            if (AllowedControllerBaseTypes.Contains(declaration.ClassName))
            {
                continue;
            }

            if (AuthorizeAttributeRegex.IsMatch(content))
            {
                continue;
            }

            violations.Add($"{ArchitectureTestPaths.ToBackendRelativePath(controllerFile)} is missing [Authorize] on controller class.");
        }

        Assert.True(
            violations.Count == 0,
            $"Controllers inheriting AuthenticatedControllerBase must declare [Authorize]:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IEnumerable<string> GetControllerFiles()
    {
        var controllersDirectory = ArchitectureTestPaths.GetBackendPath("src/Taskdeck.Api/Controllers");
        return Directory.GetFiles(controllersDirectory, "*Controller.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static ControllerDeclaration FindControllerDeclaration(string controllerFile, string content)
    {
        var match = ControllerDeclarationRegex.Match(content);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Unable to parse controller declaration in {ArchitectureTestPaths.ToBackendRelativePath(controllerFile)}.");
        }

        return new ControllerDeclaration(
            match.Groups["name"].Value,
            match.Groups["base"].Value);
    }

    private sealed record ControllerDeclaration(string ClassName, string BaseType);
}
