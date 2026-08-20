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
                    !IsAllowedRetiredMigrationMarker(path, marker) &&
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

    [Fact]
    public void Compose_ShouldMapRetiredWrapperOnlyToBooleanPresenceMarker()
    {
        const string retiredWrapper = "TASKDECK_LLM_GEMINI_API_KEY";
        const string expectedMapping =
            "TaskdeckMigration__RetiredLlmProviderConfigurationPresent: \"${TASKDECK_LLM_GEMINI_API_KEY:+true}\"";
        var repositoryRoot = Path.GetFullPath(Path.Combine(ArchitectureTestPaths.BackendRoot, ".."));
        var composePath = Path.Combine(repositoryRoot, "deploy", "docker-compose.yml");
        var compose = File.ReadAllText(composePath);

        Assert.Equal(1, CountOccurrences(compose, retiredWrapper));
        Assert.Contains(expectedMapping, compose, StringComparison.Ordinal);
        Assert.DoesNotContain($"${{{retiredWrapper}}}", compose, StringComparison.Ordinal);
        Assert.DoesNotContain($"${{{retiredWrapper}:-", compose, StringComparison.Ordinal);
        Assert.DoesNotContain($"${{{retiredWrapper}-", compose, StringComparison.Ordinal);
        Assert.DoesNotContain($"Llm__Gemini__ApiKey:", compose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeMigrationGuard_ShouldNameRetiredWrapperOnlyInFixedGuidance()
    {
        const string retiredWrapper = "TASKDECK_LLM_GEMINI_API_KEY";
        var registrationPath = ArchitectureTestPaths.GetBackendPath(
            "src/Taskdeck.Api/Extensions/LlmProviderRegistration.cs");
        var registration = File.ReadAllText(registrationPath);

        Assert.Equal(1, CountOccurrences(registration, retiredWrapper));
        Assert.Contains(
            $"The retired Docker Compose variable {retiredWrapper} is set.",
            registration,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"configuration[\"{retiredWrapper}\"]", registration, StringComparison.Ordinal);
    }

    private static bool ContainsDirectory(string path, string directoryName)
    {
        var segment = $"{Path.DirectorySeparatorChar}{directoryName}{Path.DirectorySeparatorChar}";
        return path.Contains(segment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedRetiredMigrationMarker(string path, string marker)
    {
        if (Path.GetFileName(path).Equals("playwright.demo-llm.ts", StringComparison.OrdinalIgnoreCase))
        {
            return marker.Equals("Llm__Gemini", StringComparison.OrdinalIgnoreCase)
                   || marker.Equals("TASKDECK_DEMO_GEMINI", StringComparison.OrdinalIgnoreCase)
                   || marker.Equals("TASKDECK_LLM_GEMINI", StringComparison.OrdinalIgnoreCase);
        }

        if (!marker.Equals("TASKDECK_LLM_GEMINI", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Path.GetFileName(path).Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase)
               || Path.GetFileName(path).Equals("LlmProviderRegistration.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
