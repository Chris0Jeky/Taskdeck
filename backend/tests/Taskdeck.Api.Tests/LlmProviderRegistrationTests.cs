using System.Net;
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

        provider.GetRequiredService<ILlmProvider>().GetType().FullName
            .Should().Be("Taskdeck.Application.Services.OpenAiCompatibleLlmProvider");
        var compatibleType = typeof(ILlmProvider).Assembly.GetType(
            "Taskdeck.Application.Services.OpenAiCompatibleLlmProvider",
            throwOnError: true)!;
        provider.GetService(compatibleType).Should().BeNull(
            "the selector, not a directly resolvable concrete transport, owns the live-provider decision");
        provider.GetRequiredService<IEgressRegistry>().GetAllEntries()
            .Should().ContainSingle(entry =>
                entry.Host == "api.groq.com" &&
                entry.ToolOrAgentName == "OpenAiCompatibleLlmProvider");
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(LlmProviderRegistration.OpenAiCompatibleHttpClientName);
        EnumeratePipeline(handler).Should().Contain(item => item is EgressEnvelopeHandler,
            "the configured disclosure entry must also be enforced on the compatible client");
    }

    [Fact]
    public void ProtectedSocketHandlers_ScopeDirectConnectionsToCompatibleClient()
    {
        using var existingProviderHandler = LlmProviderRegistration.CreateProtectedSocketsHttpHandler(false);
        using var compatibleHandler = LlmProviderRegistration.CreateProtectedSocketsHttpHandler(
            false,
            disableSystemProxy: true);
        using var webhookHandler = WorkerRegistration.CreateProtectedWebhookHandler(false);

        existingProviderHandler.UseProxy.Should().BeTrue(
            "OpenAI, Gemini, and Ollama retain their established system-proxy behavior");
        compatibleHandler.UseProxy.Should().BeFalse(
            "the arbitrary compatible origin must be validated directly");
        existingProviderHandler.AllowAutoRedirect.Should().BeFalse();
        compatibleHandler.AllowAutoRedirect.Should().BeFalse();
        webhookHandler.UseProxy.Should().BeTrue(
            "webhook proxy behavior is outside the compatible-provider change");
        webhookHandler.AllowAutoRedirect.Should().BeFalse();
    }

    [Fact]
    public void RegisteredPipelines_DisableProxyOnlyForCompatibleProvider()
    {
        var services = BuildCompatibleServices();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        using var openAi = factory.CreateHandler(nameof(OpenAiLlmProvider));
        using var compatible = factory.CreateHandler(LlmProviderRegistration.OpenAiCompatibleHttpClientName);
        using var gemini = factory.CreateHandler(nameof(GeminiLlmProvider));
        using var ollama = factory.CreateHandler(nameof(OllamaLlmProvider));

        EnumeratePipeline(openAi).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeTrue();
        EnumeratePipeline(compatible).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeFalse();
        EnumeratePipeline(gemini).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeTrue();
        EnumeratePipeline(ollama).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeTrue();

        var workerServices = new ServiceCollection();
        workerServices.AddLogging();
        workerServices.AddTaskdeckWorkers(
            new ConfigurationBuilder().Build(),
            new TestWebHostEnvironment("Production"));
        using var workerProvider = workerServices.BuildServiceProvider();
        using var webhook = workerProvider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler("OutboundWebhookDelivery");
        EnumeratePipeline(webhook).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task CompatibleClientPipeline_RefusesEveryRedirectWithoutFollowing(HttpStatusCode statusCode)
    {
        var services = BuildCompatibleServices();
        using var provider = services.BuildServiceProvider();
        using var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(LlmProviderRegistration.OpenAiCompatibleHttpClientName);
        var egressHandler = EnumeratePipeline(handler).OfType<EgressEnvelopeHandler>().Single();
        var redirectHandler = new RedirectStubHandler(statusCode, "https://api.groq.com/second-hop");
        egressHandler.InnerHandler = redirectHandler;
        using var invoker = new HttpMessageInvoker(handler);

        var act = () => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/chat/completions"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<EgressViolationException>();
        exception.Which.Violation.ViolationType.Should().Be(Taskdeck.Domain.Agents.EgressViolationType.RedirectNotAllowed);
        redirectHandler.InvocationCount.Should().Be(1, "the compatible pipeline must never dispatch a redirected request");
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

    private static ServiceCollection BuildCompatibleServices()
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
                ["Llm:OpenAiCompatible:Model"] = "llama-3.1-8b-instant"
            })
            .Build();
        services.AddLlmProviders(configuration);
        return services;
    }

    private sealed class RedirectStubHandler(HttpStatusCode statusCode, string location) : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            var response = new HttpResponseMessage(statusCode);
            response.Headers.Location = new Uri(location);
            return Task.FromResult(response);
        }
    }

    private static IEnumerable<HttpMessageHandler> EnumeratePipeline(HttpMessageHandler root)
    {
        for (var current = root; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
            yield return current;
    }
}
