using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LlmProviderRegistrationTests
{
    [Theory]
    [InlineData(nameof(OpenAiLlmProvider))]
    [InlineData(nameof(GeminiLlmProvider))]
    [InlineData(nameof(OllamaLlmProvider))]
    public void AddLlmProviders_ShouldDisableProxyAndRetainOriginGuards_OnFactoryPipeline(
        string clientName)
    {
        using var serviceProvider = BuildServiceProvider("Production");

        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(clientName);

        ProxySafeHttpHandlerTestHarness.AssertProxySafeOriginHandler(pipeline);
    }

    [Theory]
    [InlineData(nameof(OpenAiLlmProvider), "http://127.0.0.1/protected")]
    [InlineData(nameof(OpenAiLlmProvider), "http://10.0.0.1/protected")]
    [InlineData(nameof(OpenAiLlmProvider), "http://169.254.169.254/protected")]
    [InlineData(nameof(GeminiLlmProvider), "http://127.0.0.1/protected")]
    [InlineData(nameof(GeminiLlmProvider), "http://10.0.0.1/protected")]
    [InlineData(nameof(GeminiLlmProvider), "http://169.254.169.254/protected")]
    [InlineData(nameof(OllamaLlmProvider), "http://127.0.0.1/protected")]
    [InlineData(nameof(OllamaLlmProvider), "http://10.0.0.1/protected")]
    [InlineData(nameof(OllamaLlmProvider), "http://169.254.169.254/protected")]
    public async Task AddLlmProviders_ShouldRejectBlockedOriginWithoutConsultingHostileProxy(
        string clientName,
        string blockedOrigin)
    {
        using var serviceProvider = BuildServiceProvider("Production");
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(clientName);

        await ProxySafeHttpHandlerTestHarness.AssertBlockedOriginIgnoresProxyAsync(
            pipeline,
            blockedOrigin);
    }

    [Theory]
    [InlineData(nameof(OpenAiLlmProvider))]
    [InlineData(nameof(GeminiLlmProvider))]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task AddLlmProviders_ShouldReachAllowedDirectOriginWithoutConsultingHostileProxy(
        string clientName)
    {
        using var serviceProvider = BuildServiceProvider("Development");
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(clientName);

        await ProxySafeHttpHandlerTestHarness.AssertDirectOriginIgnoresProxyAsync(pipeline);
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

    private static ServiceProvider BuildServiceProvider(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(environmentName));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:AllowLiveProvidersInDevelopment"] = "true",
                ["Llm:Ollama:AllowLocalhostEndpoints"] = "true",
            })
            .Build();
        services.AddLlmProviders(configuration);
        return services.BuildServiceProvider();
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
}
