using System.Text.RegularExpressions;
using Xunit;

namespace Taskdeck.Architecture.Tests;

public class SourceLayerPurityTests
{
    private static readonly Regex UsingDirectiveRegex =
        new(
            @"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?(?<namespace>[A-Za-z_][A-Za-z0-9_.:]*)\s*;",
            RegexOptions.Compiled | RegexOptions.Multiline);

    [Theory]
    [MemberData(nameof(GetForbiddenImportRules))]
    public void SourceLayer_ShouldNotImportForbiddenNamespaces(
        string sourceDirectory,
        string layerDisplayName,
        string[] forbiddenNamespacePrefixes)
    {
        var directoryPath = ArchitectureTestPaths.GetBackendPath(sourceDirectory);
        var sourceFiles = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var violations = new List<string>();

        foreach (var filePath in sourceFiles)
        {
            var imports = ReadImportedNamespaces(filePath);
            foreach (var importedNamespace in imports)
            {
                if (!IsForbiddenNamespace(importedNamespace, forbiddenNamespacePrefixes))
                {
                    continue;
                }

                violations.Add(
                    $"{ArchitectureTestPaths.ToBackendRelativePath(filePath)} imports forbidden namespace '{importedNamespace}'.");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{layerDisplayName} source layer purity violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    public static IEnumerable<object[]> GetForbiddenImportRules()
    {
        yield return new object[]
        {
            "src/Taskdeck.Domain",
            "Domain",
            new[]
            {
                "Taskdeck.Application",
                "Taskdeck.Infrastructure",
                "Taskdeck.Api",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore"
            }
        };

        yield return new object[]
        {
            "src/Taskdeck.Application",
            "Application",
            new[]
            {
                "Taskdeck.Api",
                "Taskdeck.Infrastructure",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore"
            }
        };
    }

    private static IReadOnlyCollection<string> ReadImportedNamespaces(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return UsingDirectiveRegex.Matches(content)
            .Select(match => NormalizeImportedNamespace(match.Groups["namespace"].Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string NormalizeImportedNamespace(string importedNamespace)
    {
        const string globalAliasPrefix = "global::";

        return importedNamespace.StartsWith(globalAliasPrefix, StringComparison.Ordinal)
            ? importedNamespace[globalAliasPrefix.Length..]
            : importedNamespace;
    }

    private static bool IsForbiddenNamespace(string importedNamespace, IReadOnlyCollection<string> forbiddenNamespacePrefixes)
    {
        return forbiddenNamespacePrefixes.Any(prefix =>
            importedNamespace.Equals(prefix, StringComparison.Ordinal) ||
            importedNamespace.StartsWith(prefix + ".", StringComparison.Ordinal));
    }
}
