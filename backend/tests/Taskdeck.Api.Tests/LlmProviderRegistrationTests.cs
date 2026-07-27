using System.Text.Json;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
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
    [InlineData("OpenAi", typeof(OpenAiLlmProvider))]
    [InlineData("Gemini", typeof(GeminiLlmProvider))]
    [InlineData("Ollama", typeof(OllamaLlmProvider))]
    public async Task AddLlmProviders_ShouldApplyResolvedLocalhostPolicyToConcreteProvider(
        string providerName,
        Type expectedProviderType)
    {
        using var serviceProvider = BuildServiceProvider("Development", providerName);
        using var scope = serviceProvider.CreateScope();

        var runtimePolicy = serviceProvider.GetRequiredService<LlmProviderRuntimePolicy>();
        var provider = scope.ServiceProvider.GetRequiredService<ILlmProvider>();
        var health = await provider.GetHealthAsync();

        runtimePolicy.AllowGeneralProviderLocalhost.Should().BeTrue();
        runtimePolicy.AllowOllamaLocalhost.Should().BeTrue();
        runtimePolicy.ProtectOutboundTelemetry.Should().BeTrue(
            "registered provider clients must mask destinations before HttpClient diagnostics run");
        provider.Should().BeOfType(expectedProviderType);
        health.IsAvailable.Should().BeTrue(
            "the selected concrete provider must reuse the same localhost policy as selection and connect-time validation");
    }

    [Theory]
    [InlineData("OpenAi", false, "/v1/chat/completions")]
    [InlineData("OpenAi", true, "/v1/chat/completions")]
    [InlineData("Gemini", false, "/v1beta/models/test-gemini-model:generateContent")]
    [InlineData("Gemini", true, "/v1beta/models/test-gemini-model:generateContent")]
    [InlineData("Ollama", false, "/api/chat")]
    [InlineData("Ollama", true, "/api/chat")]
    public async Task AddLlmProviders_ShouldDispatchProbeAndCompletionThroughRegisteredLoopbackPipeline(
        string providerName,
        bool probe,
        string expectedPath)
    {
        await using var server = new SingleRequestLoopbackServer(
            responseBody: BuildProviderResponse(providerName));
        var origin = $"http://localhost:{server.Port}";
        var providerBaseUrl = providerName switch
        {
            "OpenAi" => $"{origin}/v1",
            "Gemini" => $"{origin}/v1beta",
            _ => origin
        };
        using var serviceProvider = BuildServiceProvider(
            "Development",
            providerName,
            providerBaseUrl: providerBaseUrl);
        using var scope = serviceProvider.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ILlmProvider>();

        if (probe)
        {
            var health = await provider.ProbeAsync();
            health.IsAvailable.Should().BeTrue();
            health.IsProbed.Should().BeTrue();
        }
        else
        {
            var result = await provider.CompleteAsync(new ChatCompletionRequest(
                [new ChatCompletionMessage("User", "loopback dispatch")],
                SystemPrompt: string.Empty));
            result.IsDegraded.Should().BeFalse();
        }

        var rawRequest = await server.ReceivedRequest;
        var requestBody = await server.ReceivedBody;
        rawRequest.Should().StartWith($"POST {expectedPath} HTTP/1.1",
            "the concrete provider must dispatch through its registered protected HttpClient");
        AssertProviderRequestBody(requestBody, providerName, probe);
    }

    [Fact]
    public async Task AddLlmProviders_ShouldInjectProductionPolicyIntoConcreteOllamaProvider()
    {
        await using var server = new SingleRequestLoopbackServer(
            responseBody: BuildProviderResponse("Ollama"));
        using var serviceProvider = BuildServiceProvider(
            "Production",
            "Ollama",
            providerBaseUrl: $"http://localhost:{server.Port}");
        using var scope = serviceProvider.CreateScope();
        var settings = serviceProvider.GetRequiredService<LlmProviderSettings>();
        var runtimePolicy = serviceProvider.GetRequiredService<LlmProviderRuntimePolicy>();
        var provider = scope.ServiceProvider.GetRequiredService<OllamaLlmProvider>();

        var health = await provider.GetHealthAsync();
        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "must remain local")],
            SystemPrompt: string.Empty));

        settings.Ollama.AllowLocalhostEndpoints.Should().BeTrue(
            "the regression must prove the resolved policy overrides the raw opt-in");
        runtimePolicy.AllowOllamaLocalhost.Should().BeFalse();
        health.IsAvailable.Should().BeFalse(
            "Production must override the raw Ollama localhost opt-in");
        result.IsDegraded.Should().BeTrue(
            "the injected Production policy must reject localhost before dispatch");
        server.ReceivedRequest.IsCompleted.Should().BeFalse(
            "the concrete provider must not contact localhost when the resolved runtime policy denies it");
    }

    [Theory]
    [InlineData(nameof(OpenAiLlmProvider))]
    [InlineData(nameof(GeminiLlmProvider))]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task AddLlmProviders_ShouldSuppressProtectedRequestLogging(string clientName)
    {
        var loggerProvider = new RecordingHttpLoggerProvider();
        using var serviceProvider = BuildServiceProvider("Production", loggerProvider: loggerProvider);
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(clientName);

        await ProxySafeHttpHandlerTestHarness.AssertBlockedOriginIgnoresProxyAsync(
            pipeline,
            "http://127.0.0.1/protected");

        loggerProvider.Messages.Should().NotContain(
            message => message.Contains(ProxySafeHttpHandlerTestHarness.SensitiveMarker, StringComparison.Ordinal),
            "protected query/header/body markers must not reach default IHttpClientFactory logs");
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

    private static ServiceProvider BuildServiceProvider(
        string environmentName,
        string providerName = "Mock",
        ILoggerProvider? loggerProvider = null,
        string? providerBaseUrl = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            if (loggerProvider is not null)
            {
                builder.AddProvider(loggerProvider);
            }
        });
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(environmentName));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:EnableLiveProviders"] = "true",
                ["Llm:AllowLiveProvidersInDevelopment"] = "true",
                ["Llm:Provider"] = providerName,
                ["Llm:OpenAi:ApiKey"] = "test-openai-key",
                ["Llm:OpenAi:BaseUrl"] = providerName == "OpenAi" && providerBaseUrl is not null
                    ? providerBaseUrl
                    : "http://localhost:12345",
                ["Llm:OpenAi:Model"] = "test-openai-model",
                ["Llm:Gemini:ApiKey"] = "test-gemini-key",
                ["Llm:Gemini:BaseUrl"] = providerName == "Gemini" && providerBaseUrl is not null
                    ? providerBaseUrl
                    : "http://localhost:12345",
                ["Llm:Gemini:Model"] = "test-gemini-model",
                ["Llm:Ollama:BaseUrl"] = providerName == "Ollama" && providerBaseUrl is not null
                    ? providerBaseUrl
                    : "http://localhost:12345",
                ["Llm:Ollama:Model"] = "test-ollama-model",
                ["Llm:Ollama:AllowLocalhostEndpoints"] = "true",
            })
            .Build();
        services.AddLlmProviders(configuration);
        return services.BuildServiceProvider();
    }

    private static string BuildProviderResponse(string providerName) => providerName switch
    {
        "OpenAi" =>
            """
            {"choices":[{"message":{"content":"OK"},"finish_reason":"stop"}],"usage":{"total_tokens":1}}
            """,
        "Gemini" =>
            """
            {"candidates":[{"content":{"parts":[{"text":"OK"}]},"finishReason":"STOP"}],"usageMetadata":{"totalTokenCount":1}}
            """,
        "Ollama" =>
            """
            {"message":{"content":"OK"},"done":true,"eval_count":1,"done_reason":"stop"}
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
    };

    private static void AssertProviderRequestBody(string requestBody, string providerName, bool probe)
    {
        requestBody.Should().NotBeNullOrWhiteSpace(
            "the loopback server must read the complete provider request body before responding");
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;
        var expectedContent = probe ? "Reply with exactly: OK" : "loopback dispatch";
        var expectedMaxTokens = probe ? 4 : 2048;
        var expectedTemperature = probe ? 0 : 0.7;

        switch (providerName)
        {
            case "OpenAi":
                root.GetProperty("model").GetString().Should().Be("test-openai-model");
                root.GetProperty("stream").GetBoolean().Should().BeFalse();
                root.GetProperty("max_tokens").GetInt32().Should().Be(expectedMaxTokens);
                root.GetProperty("temperature").GetDouble().Should().BeApproximately(expectedTemperature, 0.000001);
                root.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be(expectedContent);
                break;
            case "Gemini":
                var generationConfig = root.GetProperty("generationConfig");
                generationConfig.GetProperty("maxOutputTokens").GetInt32().Should().Be(expectedMaxTokens);
                generationConfig.GetProperty("temperature").GetDouble().Should().BeApproximately(expectedTemperature, 0.000001);
                root.GetProperty("contents")[0]
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString()
                    .Should()
                    .Be(expectedContent);
                break;
            case "Ollama":
                root.GetProperty("model").GetString().Should().Be("test-ollama-model");
                root.GetProperty("stream").GetBoolean().Should().BeFalse();
                var options = root.GetProperty("options");
                options.GetProperty("num_predict").GetInt32().Should().Be(expectedMaxTokens);
                options.GetProperty("temperature").GetDouble().Should().BeApproximately(expectedTemperature, 0.000001);
                root.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be(expectedContent);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null);
        }
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
