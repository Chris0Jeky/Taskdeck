using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Http;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LlmProviderRegistrationTests
{
    [Fact]
    public void AddLlmProviders_ResolvesOpenAiCompatibleProvider_WhenSelectionIsValid()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment("Production"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:EnableLiveProviders"] = "true",
                ["Llm:Provider"] = "OpenAICompatible",
                ["Llm:OpenAiCompatible:ApiKey"] = "test-compatible-key",
                ["Llm:OpenAiCompatible:BaseUrl"] = "https://api.groq.com/openai/v1",
                ["Llm:OpenAiCompatible:Model"] = "llama-3.1-8b-instant",
                ["Llm:OpenAiCompatible:TimeoutSeconds"] = "30"
            })
            .Build();

        services.AddLlmProviders(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<Taskdeck.Application.Services.ILlmProvider>()
            .Should().BeOfType<Taskdeck.Application.Services.OpenAiCompatibleLlmProvider>();
        provider.GetService<OpenAiCompatibleLlmProvider>().Should().BeNull(
            "the selector, not a directly resolvable concrete transport, owns the live-provider decision");
        provider.GetRequiredService<IEgressRegistry>().GetAllEntries()
            .Should().ContainSingle(entry =>
                entry.Host == "api.groq.com" &&
                entry.ToolOrAgentName == nameof(OpenAiCompatibleLlmProvider));
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(LlmProviderRegistration.OpenAiCompatibleHttpClientName);
        EnumeratePipeline(handler).Should().Contain(item => item is EgressEnvelopeHandler,
            "the configured disclosure entry must also be enforced on the compatible client");
    }

    [Fact]
    public void ProtectedSocketHandlers_DisableSystemProxyBypass()
    {
        using var llmHandler = LlmProviderRegistration.CreateProtectedSocketsHttpHandler(false);
        using var webhookHandler = WorkerRegistration.CreateProtectedWebhookHandler(false);

        llmHandler.UseProxy.Should().BeFalse();
        llmHandler.AllowAutoRedirect.Should().BeFalse();
        webhookHandler.UseProxy.Should().BeFalse();
        webhookHandler.AllowAutoRedirect.Should().BeFalse();
    }

    [Theory]
    [InlineData("Development", true, true, true)]
    [InlineData("Development", true, false, false)]
    [InlineData("Test", true, true, true)]
    [InlineData("Testing", true, true, true)]
    [InlineData("Production", true, true, false)]
    [InlineData("Development", false, true, false)]
    public void ResolveLocalhostPolicy_RequiresDevelopmentLiveProvidersAndOllamaOptIn(
        string environmentName,
        bool allowLiveProvidersInDevelopment,
        bool allowOllamaLocalhost,
        bool expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(environmentName));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:AllowLiveProvidersInDevelopment"] = allowLiveProvidersInDevelopment.ToString(),
                ["Llm:Ollama:AllowLocalhostEndpoints"] = allowOllamaLocalhost.ToString(),
            })
            .Build();

        var result = LlmProviderRegistration.ResolveLocalhostPolicy(services, configuration);

        var expectedGeneralProviderLocalhost = allowLiveProvidersInDevelopment &&
            (environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
             environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
             environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase));

        result.AllowGeneralProviderLocalhost.Should().Be(expectedGeneralProviderLocalhost);
        result.AllowOllamaLocalhost.Should().Be(expected);
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Taskdeck.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = environmentName;
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IEnumerable<HttpMessageHandler> EnumeratePipeline(HttpMessageHandler root)
    {
        for (var current = root; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
            yield return current;
    }
}
