using System.Text.RegularExpressions;
using Xunit;

namespace Taskdeck.Architecture.Tests;

/// <summary>
/// ADR-0065 Decision 1 / CF-01 (#2255): <c>CaptureIntakeService</c> is the one canonical writer of
/// the durable <c>Capture</c> aggregate. Every path that admits a capture goes through it, so the
/// source assets, the provenance dimensions, the state axes and the ID-preserving contract are
/// decided in exactly one place and cannot drift between the composer, the queue endpoint, the MCP
/// tool and the backfill.
/// <para>
/// This is a source scan rather than a runtime assertion because the invariant is about what code
/// may exist at all: a new creation path that builds its own <c>Capture</c> would satisfy every
/// runtime test while quietly forking the rules. The allowlist below is the complete set of files
/// permitted to construct the aggregate, and adding to it is a deliberate decision.
/// </para>
/// </summary>
public class CaptureIntakeSingleWriterTests
{
    /// <summary>
    /// The only files allowed to construct the aggregate: the canonical intake (which builds it),
    /// the entity itself (its own factory), and the tests that exercise them.
    /// </summary>
    private static readonly string[] AllowedConstructionFiles =
    {
        "src/Taskdeck.Domain/Entities/Capture.cs",
        "src/Taskdeck.Application/Services/CaptureIntakeService.cs"
    };

    /// <summary>
    /// The only files allowed to add a capture to the persistence set: the store that implements the
    /// facade the intake and the backfill write through.
    /// </summary>
    private static readonly string[] AllowedPersistenceFiles =
    {
        "src/Taskdeck.Infrastructure/Repositories/EfCaptureStore.cs"
    };

    private static readonly Regex CaptureConstructionRegex = new(
        @"new\s+Capture\s*\(|Capture\.FromQueueRequest\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex CapturePersistenceRegex = new(
        @"\.Captures\s*\.\s*(Add|AddAsync|AddRange|AddRangeAsync|Attach|Update)\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void OnlyTheCanonicalIntakeConstructsTheCaptureAggregate()
    {
        var violations = ScanProductionSources(CaptureConstructionRegex, AllowedConstructionFiles);

        Assert.True(
            violations.Count == 0,
            "Only CaptureIntakeService may construct a Capture (ADR-0065 Decision 1; CF-01 #2255). " +
            "Route the new path through CaptureIntakeService.IntakeAsync or BuildCapture:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void OnlyTheCaptureStoreWritesTheCapturesSet()
    {
        var violations = ScanProductionSources(CapturePersistenceRegex, AllowedPersistenceFiles);

        Assert.True(
            violations.Count == 0,
            "Only EfCaptureStore may write the Captures set; every other writer must go through " +
            "ICaptureStore so writes stay owner-scoped and stage into the ambient unit of work:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static List<string> ScanProductionSources(Regex pattern, string[] allowedRelativePaths)
    {
        var allowed = new HashSet<string>(allowedRelativePaths, StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();

        foreach (var projectDirectory in new[]
                 {
                     "src/Taskdeck.Domain",
                     "src/Taskdeck.Application",
                     "src/Taskdeck.Infrastructure",
                     "src/Taskdeck.Api",
                     "src/Taskdeck.Cli"
                 })
        {
            var directoryPath = ArchitectureTestPaths.GetBackendPath(projectDirectory);
            if (!Directory.Exists(directoryPath))
            {
                continue;
            }

            foreach (var filePath in Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories))
            {
                if (filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    filePath.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = ArchitectureTestPaths.ToBackendRelativePath(filePath);
                if (allowed.Contains(relativePath))
                {
                    continue;
                }

                var text = File.ReadAllText(filePath);
                if (pattern.IsMatch(text))
                {
                    violations.Add($"{relativePath} matches {pattern}");
                }
            }
        }

        return violations;
    }
}
