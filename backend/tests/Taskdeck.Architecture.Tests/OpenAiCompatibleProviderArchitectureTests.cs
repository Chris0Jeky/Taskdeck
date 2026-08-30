using System.Reflection;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Architecture.Tests;

public class OpenAiCompatibleProviderArchitectureTests
{
    [Fact]
    public void ProviderTransport_IsNotPubliclyConstructible()
    {
        var providerType = typeof(ILlmProvider).Assembly.GetType(
            "Taskdeck.Application.Services.OpenAiCompatibleLlmProvider",
            throwOnError: true)!;

        Assert.False(providerType.IsPublic);
        Assert.Empty(providerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.NotEmpty(providerType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));

        var apiRoot = ArchitectureTestPaths.GetBackendPath("src/Taskdeck.Api");
        var apiConstructionSites = Directory.GetFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "new OpenAiCompatibleLlmProvider(",
                StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();
        Assert.Equal(new[] { "LlmProviderRegistration.cs" }, apiConstructionSites);
    }

    [Fact]
    public void DeploymentSurfaces_ExposeTheSameCompatibleProviderSettingsWithoutASecretValue()
    {
        var repoRoot = Directory.GetParent(ArchitectureTestPaths.BackendRoot)!.FullName;
        var compose = ReadRepoFile(repoRoot, "deploy/docker-compose.yml");
        var envExample = ReadRepoFile(repoRoot, "deploy/.env.example");
        var render = ReadRepoFile(repoRoot, "deploy/render.yaml");
        var productionTemplate = ReadRepoFile(repoRoot, "deploy/.env.production.template");

        var composeMappings = new[]
        {
            "Llm__OpenAiCompatible__ApiKey: ${TASKDECK_LLM_OPENAI_COMPATIBLE_API_KEY:-}",
            "Llm__OpenAiCompatible__BaseUrl: ${TASKDECK_LLM_OPENAI_COMPATIBLE_BASE_URL:-}",
            "Llm__OpenAiCompatible__Model: ${TASKDECK_LLM_OPENAI_COMPATIBLE_MODEL:-}",
            "Llm__OpenAiCompatible__TimeoutSeconds: ${TASKDECK_LLM_OPENAI_COMPATIBLE_TIMEOUT_SECONDS:-30}",
            "Llm__OpenAiCompatible__MaxResponseBytes: ${TASKDECK_LLM_OPENAI_COMPATIBLE_MAX_RESPONSE_BYTES:-1048576}",
            "Llm__OpenAiCompatible__MaxSseLineBytes: ${TASKDECK_LLM_OPENAI_COMPATIBLE_MAX_SSE_LINE_BYTES:-65536}",
            "Llm__OpenAiCompatible__MaxSseEventBytes: ${TASKDECK_LLM_OPENAI_COMPATIBLE_MAX_SSE_EVENT_BYTES:-131072}",
            "Llm__OpenAiCompatible__ExtraHeaders__HTTP-Referer: ${TASKDECK_LLM_OPENAI_COMPATIBLE_HTTP_REFERER:-}",
            "Llm__OpenAiCompatible__ExtraHeaders__X-Title: ${TASKDECK_LLM_OPENAI_COMPATIBLE_X_TITLE:-}"
        };
        Assert.All(composeMappings, mapping => Assert.Contains(mapping, compose, StringComparison.Ordinal));

        var externalNames = new[]
        {
            "TASKDECK_LLM_OPENAI_COMPATIBLE_API_KEY=",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_BASE_URL=",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_MODEL=",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_TIMEOUT_SECONDS=30",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_MAX_RESPONSE_BYTES=1048576",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_MAX_SSE_LINE_BYTES=65536",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_MAX_SSE_EVENT_BYTES=131072",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_HTTP_REFERER=",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_X_TITLE="
        };
        Assert.All(externalNames, name => Assert.Contains(name, envExample, StringComparison.Ordinal));

        var appSettingNames = new[]
        {
            "Llm__OpenAiCompatible__ApiKey",
            "Llm__OpenAiCompatible__BaseUrl",
            "Llm__OpenAiCompatible__Model",
            "Llm__OpenAiCompatible__TimeoutSeconds",
            "Llm__OpenAiCompatible__MaxResponseBytes",
            "Llm__OpenAiCompatible__MaxSseLineBytes",
            "Llm__OpenAiCompatible__MaxSseEventBytes",
            "Llm__OpenAiCompatible__ExtraHeaders__HTTP-Referer",
            "Llm__OpenAiCompatible__ExtraHeaders__X-Title"
        };
        Assert.All(appSettingNames, name => Assert.Contains(name, render, StringComparison.Ordinal));
        Assert.All(appSettingNames, name => Assert.Contains(name, productionTemplate, StringComparison.Ordinal));

        Assert.DoesNotContain("TASKDECK_LLM_OPENAI_COMPATIBLE_API_KEY=sk-", envExample, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Llm__OpenAiCompatible__ApiKey=sk-", productionTemplate, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepoFile(string repoRoot, string relativePath) =>
        File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
