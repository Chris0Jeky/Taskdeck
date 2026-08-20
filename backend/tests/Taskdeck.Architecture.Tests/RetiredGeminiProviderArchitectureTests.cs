using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Architecture.Tests;

public class RetiredGeminiProviderArchitectureTests
{
    private static readonly string[] RetiredIntegrationMarkers =
    [
        "GeminiLlmProvider",
        "GeminiProviderSettings",
        "LlmProviderKind.Gemini",
        "TryValidateGeminiSettings",
        "generativelanguage.googleapis.com",
        "Llm__Gemini",
        "TASKDECK_DEMO_GEMINI",
        "TASKDECK_LLM_GEMINI"
    ];

    [Fact]
    public void ApplicationAssembly_ShouldNotExposeRetiredGeminiProviderTypes()
    {
        var applicationAssembly = typeof(ILlmProvider).Assembly;

        Assert.Null(applicationAssembly.GetType(
            "Taskdeck.Application.Services.GeminiLlmProvider",
            throwOnError: false));
        Assert.Null(applicationAssembly.GetType(
            "Taskdeck.Application.Services.GeminiProviderSettings",
            throwOnError: false));
        Assert.DoesNotContain(
            Enum.GetNames<LlmProviderKind>(),
            name => name.Equals("Gemini", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, (int)LlmProviderKind.Ollama);
        Assert.Equal(4, (int)LlmProviderKind.OpenAiCompatible);
    }

    [Fact]
    public void ActiveRuntimeAndDeploymentSurfaces_ShouldNotReintroduceGeminiIntegration()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(ArchitectureTestPaths.BackendRoot, ".."));
        var files = Directory
            .GetFiles(
                ArchitectureTestPaths.GetBackendPath("src"),
                "*",
                SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".json")
            .Where(path => !ContainsDirectory(path, "bin") && !ContainsDirectory(path, "obj"))
            .Concat(
            [
                Path.Combine(repositoryRoot, ".gitleaks.toml"),
                Path.Combine(repositoryRoot, "deploy", ".env.example"),
                Path.Combine(repositoryRoot, "deploy", ".env.production.template"),
                Path.Combine(repositoryRoot, "deploy", "docker-compose.yml"),
                Path.Combine(repositoryRoot, "deploy", "render.yaml"),
                Path.Combine(repositoryRoot, "scripts", "security", "drill-key-rotation.sh"),
                Path.Combine(repositoryRoot, "frontend", "taskdeck-web", "playwright.demo-llm.ts")
            ])
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var violations = files
            .SelectMany(path => RetiredIntegrationMarkers
                .Where(marker =>
                    !IsDemoMigrationGuardMarker(path, marker) &&
                    File.ReadAllText(path).Contains(marker, StringComparison.OrdinalIgnoreCase))
                .Select(marker =>
                    $"{Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/')}: {marker}"))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Retired Gemini integration markers remain in active runtime/deployment surfaces:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static bool ContainsDirectory(string path, string directoryName)
    {
        var segment = $"{Path.DirectorySeparatorChar}{directoryName}{Path.DirectorySeparatorChar}";
        return path.Contains(segment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDemoMigrationGuardMarker(string path, string marker)
    {
        return Path.GetFileName(path).Equals("playwright.demo-llm.ts", StringComparison.OrdinalIgnoreCase)
               && (marker.Equals("Llm__Gemini", StringComparison.OrdinalIgnoreCase)
                   || marker.Equals("TASKDECK_DEMO_GEMINI", StringComparison.OrdinalIgnoreCase)
                   || marker.Equals("TASKDECK_LLM_GEMINI", StringComparison.OrdinalIgnoreCase));
    }
}
