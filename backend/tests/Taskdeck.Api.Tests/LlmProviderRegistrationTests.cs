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
    [Theory]
    [InlineData("Gemini")]
    [InlineData("gemini")]
    [InlineData(" GEMINI ")]
    public void AddLlmProviders_ShouldRejectRetiredGeminiSelector(string selector)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = selector
            })
            .Build();

        var act = () => services.AddLlmProviders(configuration);

        var exception = act.Should().Throw<RetiredLlmProviderConfigurationException>().Which;
        exception.Reason.Should().Be(RetiredLlmProviderConfigurationReason.ProviderSelector);
        exception.Message.Should().Contain("Gemini provider support was removed");
        exception.Message.Should().Contain("OpenAi");
        exception.Message.Should().Contain("OpenAiCompatible");
        exception.Message.Should().Contain("Ollama");
        exception.Message.Should().Contain("Mock");
    }

    [Theory]
    [InlineData("Mock")]
    [InlineData("OpenAI")]
    [InlineData("OpenAICompatible")]
    [InlineData("Ollama")]
    public void AddLlmProviders_ShouldIgnoreRetiredGeminiSection_WhenSupportedProviderIsExplicit(
        string provider)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = provider,
                ["Llm:Gemini:ApiKey"] = "stale-test-key"
            })
            .Build();

        var act = () => services.AddLlmProviders(configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddLlmProviders_ShouldRejectRetiredGeminiSection_WhenBuiltInMockIsTheOnlySelector()
    {
        var services = new ServiceCollection();
        var configuration = BuildRealProviderConfiguration();

        configuration["Llm:Provider"].Should().Be("Mock");
        var act = () => services.AddLlmProviders(configuration);

        var exception = act.Should().Throw<RetiredLlmProviderConfigurationException>().Which;
        exception.Reason.Should().Be(RetiredLlmProviderConfigurationReason.SettingsSection);
    }

    [Fact]
    public void AddLlmProviders_ShouldIgnoreRetiredGeminiSection_WhenEnvironmentExplicitlySelectsMock()
    {
        var services = new ServiceCollection();
        var configuration = BuildRealProviderConfiguration("Mock");

        var act = () => services.AddLlmProviders(configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddLlmProviders_ShouldRejectRetiredGeminiSelector_WhenEnvironmentExplicitlySelectsIt()
    {
        var services = new ServiceCollection();
        var configuration = BuildRealProviderConfiguration("Gemini");

        var act = () => services.AddLlmProviders(configuration);

        var exception = act.Should().Throw<RetiredLlmProviderConfigurationException>().Which;
        exception.Reason.Should().Be(RetiredLlmProviderConfigurationReason.ProviderSelector);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("historical-free-form-provider")]
    public void AddLlmProviders_ShouldRejectRetiredGeminiSection_WithoutExplicitSupportedProvider(
        string? provider)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = provider,
                ["Llm:Gemini:ApiKey"] = "stale-test-key"
            })
            .Build();

        var act = () => services.AddLlmProviders(configuration);

        var exception = act.Should().Throw<RetiredLlmProviderConfigurationException>().Which;
        exception.Reason.Should().Be(RetiredLlmProviderConfigurationReason.SettingsSection);
        exception.Message.Should().Contain("Gemini provider support was removed");
        exception.Message.Should().Contain("remove the retired Gemini settings section");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("TrUe")]
    public void AddLlmProviders_ShouldRejectRetiredComposeWrapperPresenceMarker(string marker)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TaskdeckMigration:RetiredLlmProviderConfigurationPresent"] = marker,
                ["Llm:Provider"] = "Mock"
            })
            .Build();

        var act = () => services.AddLlmProviders(configuration);

        var exception = act.Should().Throw<RetiredLlmProviderConfigurationException>().Which;
        exception.Reason.Should().Be(RetiredLlmProviderConfigurationReason.ComposeMarker);
        exception.Message.Should().Contain("TASKDECK_LLM_GEMINI_API_KEY");
        exception.Message.Should().Contain("TASKDECK_LLM_OPENAI_API_KEY");
        exception.Message.Should().Contain("OpenAICompatible");
        exception.Message.Should().Contain("Ollama");
        exception.Message.Should().Contain("Mock");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData(" true ")]
    public void AddLlmProviders_ShouldIgnoreInactiveRetiredComposeWrapperPresenceMarker(string? marker)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TaskdeckMigration:RetiredLlmProviderConfigurationPresent"] = marker,
                ["Llm:Provider"] = "Mock"
            })
            .Build();

        var act = () => services.AddLlmProviders(configuration);

        act.Should().NotThrow();
    }

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
    public void RegisteredPipelines_KeepCompatibleProviderInsideProtectedDirectEgressBoundary()
    {
        var services = BuildCompatibleServices();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        using var openAi = factory.CreateHandler(nameof(OpenAiLlmProvider));
        using var compatible = factory.CreateHandler(LlmProviderRegistration.OpenAiCompatibleHttpClientName);
        using var ollama = factory.CreateHandler(nameof(OllamaLlmProvider));

        EnumeratePipeline(openAi).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeFalse();
        EnumeratePipeline(compatible).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeFalse();
        EnumeratePipeline(ollama).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeFalse();
        ProxySafeHttpHandlerTestHarness.AssertProxySafeOriginHandler(compatible);
        EnumeratePipeline(compatible).Should().Contain(item => item is EgressEnvelopeHandler);
        EnumeratePipeline(compatible).Select(item => item.GetType().Name).Should().ContainInOrder(
            "PolicyHttpMessageHandler",
            nameof(ProtectedOutboundTelemetryHandler),
            nameof(EgressEnvelopeHandler),
            "LlmDispatchTrackingHandler",
            nameof(SocketsHttpHandler));

        var workerServices = new ServiceCollection();
        workerServices.AddLogging();
        workerServices.AddTaskdeckWorkers(
            new ConfigurationBuilder().Build(),
            new TestWebHostEnvironment("Production"));
        using var workerProvider = workerServices.BuildServiceProvider();
        using var webhook = workerProvider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler("OutboundWebhookDelivery");
        EnumeratePipeline(webhook).OfType<SocketsHttpHandler>().Single().UseProxy.Should().BeFalse();
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
        const string sensitiveMarker = "must-not-appear-in-egress-audit";
        var redirectHandler = new RedirectStubHandler(
            statusCode,
            $"https://api.groq.com/second-hop?marker={sensitiveMarker}");
        egressHandler.InnerHandler = redirectHandler;
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.groq.com/openai/v1/chat/completions?marker={sensitiveMarker}");
        ProtectedOutboundTelemetryHandler.PrepareForSend(request);
        var act = () => invoker.SendAsync(request, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<EgressViolationException>();
        exception.Which.Violation.ViolationType.Should().Be(Taskdeck.Domain.Agents.EgressViolationType.RedirectNotAllowed);
        exception.Which.Violation.RequestUri.Should().Be("https://api.groq.com");
        exception.Which.ToString().Should().NotContain(sensitiveMarker);
        request.RequestUri!.Host.Should().Be("protected-outbound.invalid",
            "the protected request must be remasked even when the egress boundary throws");
        redirectHandler.InvocationCount.Should().Be(1, "the compatible pipeline must never dispatch a redirected request");
    }

    [Theory]
    [InlineData(nameof(OpenAiLlmProvider))]
    [InlineData(LlmProviderRegistration.OpenAiCompatibleHttpClientName)]
    [InlineData(nameof(OllamaLlmProvider))]
    public void AddLlmProviders_ShouldDisableProxyAndRetainOriginGuards_OnFactoryPipeline(
        string clientName)
    {
        using var serviceProvider = BuildServiceProvider(
            "Production",
            clientName == LlmProviderRegistration.OpenAiCompatibleHttpClientName
                ? "OpenAiCompatible"
                : "Mock");

        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(clientName);

        ProxySafeHttpHandlerTestHarness.AssertProxySafeOriginHandler(pipeline);
    }

    [Theory]
    [InlineData(nameof(OpenAiLlmProvider), "http://127.0.0.1/protected")]
    [InlineData(nameof(OpenAiLlmProvider), "http://10.0.0.1/protected")]
    [InlineData(nameof(OpenAiLlmProvider), "http://169.254.169.254/protected")]
    [InlineData(LlmProviderRegistration.OpenAiCompatibleHttpClientName, "http://127.0.0.1/protected")]
    [InlineData(LlmProviderRegistration.OpenAiCompatibleHttpClientName, "http://10.0.0.1/protected")]
    [InlineData(LlmProviderRegistration.OpenAiCompatibleHttpClientName, "http://169.254.169.254/protected")]
    [InlineData(nameof(OllamaLlmProvider), "http://127.0.0.1/protected")]
    [InlineData(nameof(OllamaLlmProvider), "http://10.0.0.1/protected")]
    [InlineData(nameof(OllamaLlmProvider), "http://169.254.169.254/protected")]
    public async Task AddLlmProviders_ShouldRejectBlockedOriginWithoutConsultingHostileProxy(
        string clientName,
        string blockedOrigin)
    {
        using var serviceProvider = BuildServiceProvider(
            "Production",
            clientName == LlmProviderRegistration.OpenAiCompatibleHttpClientName
                ? "OpenAiCompatible"
                : "Mock");
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(clientName);

        await ProxySafeHttpHandlerTestHarness.AssertBlockedOriginIgnoresProxyAsync(
            pipeline,
            blockedOrigin,
            expectStructuredEgressViolation:
                clientName == LlmProviderRegistration.OpenAiCompatibleHttpClientName);
    }

    [Theory]
    [InlineData(nameof(OpenAiLlmProvider))]
    [InlineData(LlmProviderRegistration.OpenAiCompatibleHttpClientName)]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task AddLlmProviders_ShouldReachAllowedDirectOriginWithoutConsultingHostileProxy(
        string clientName)
    {
        using var serviceProvider = BuildServiceProvider(
            "Development",
            clientName == LlmProviderRegistration.OpenAiCompatibleHttpClientName
                ? "OpenAiCompatible"
                : "Mock");
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(clientName);

        await ProxySafeHttpHandlerTestHarness.AssertDirectOriginIgnoresProxyAsync(pipeline);
    }

    [Theory]
    [InlineData("OpenAi", nameof(OpenAiLlmProvider))]
    [InlineData("OpenAiCompatible", "OpenAiCompatibleLlmProvider")]
    [InlineData("Ollama", nameof(OllamaLlmProvider))]
    public async Task AddLlmProviders_ShouldApplyResolvedLocalhostPolicyToConcreteProvider(
        string providerName,
        string expectedProviderType)
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
        provider.GetType().Name.Should().Be(expectedProviderType);
        health.IsAvailable.Should().BeTrue(
            "the selected concrete provider must reuse the same localhost policy as selection and connect-time validation");
    }

    [Theory]
    [InlineData("OpenAi", false, "/v1/chat/completions")]
    [InlineData("OpenAi", true, "/v1/chat/completions")]
    [InlineData("OpenAiCompatible", false, "/openai/v1/chat/completions")]
    [InlineData("OpenAiCompatible", true, "/openai/v1/chat/completions")]
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
            "OpenAiCompatible" => $"{origin}/openai/v1",
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
    public async Task AddLlmProviders_CompatibleStreamUsesRegisteredLoopbackPipelineIncrementally()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"lo\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: {\"choices\":[],\"usage\":{\"total_tokens\":7}}\n\n" +
            "data: [DONE]\n\n";
        await using var server = new SingleRequestLoopbackServer(
            responseBody: sse,
            responseContentType: "text/event-stream");
        using var serviceProvider = BuildServiceProvider(
            "Development",
            "OpenAiCompatible",
            providerBaseUrl: $"http://localhost:{server.Port}/openai/v1");
        using var scope = serviceProvider.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ILlmProvider>();

        var events = new List<LlmTokenEvent>();
        await foreach (var item in provider.StreamAsync(new ChatCompletionRequest(
                           [new ChatCompletionMessage("User", "stream loopback")],
                           SystemPrompt: string.Empty)))
        {
            events.Add(item);
        }

        events.Select(item => item.Token).Should().Equal("Hel", "lo", string.Empty);
        events[^1].TokensUsed.Should().Be(7);
        (await server.ReceivedRequest).Should().StartWith(
            "POST /openai/v1/chat/completions HTTP/1.1");
        using var payload = JsonDocument.Parse(await server.ReceivedBody);
        payload.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
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
    [InlineData(LlmProviderRegistration.OpenAiCompatibleHttpClientName)]
    [InlineData(nameof(OllamaLlmProvider))]
    public async Task AddLlmProviders_ShouldSuppressProtectedRequestLogging(string clientName)
    {
        var loggerProvider = new RecordingHttpLoggerProvider();
        using var serviceProvider = BuildServiceProvider(
            "Production",
            clientName == LlmProviderRegistration.OpenAiCompatibleHttpClientName
                ? "OpenAiCompatible"
                : "Mock",
            loggerProvider: loggerProvider);
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(clientName);

        await ProxySafeHttpHandlerTestHarness.AssertBlockedOriginIgnoresProxyAsync(
            pipeline,
            "http://127.0.0.1/protected",
            expectStructuredEgressViolation:
                clientName == LlmProviderRegistration.OpenAiCompatibleHttpClientName);

        loggerProvider.Messages.Should().NotContain(
            message => message.Contains(ProxySafeHttpHandlerTestHarness.SensitiveMarker, StringComparison.Ordinal),
            "protected query/header/body markers must not reach default IHttpClientFactory logs");
    }

    [Fact]
    public async Task CompatibleRegisteredPolicy_Http501StillReachesBufferedFallbackAtThresholdOne()
    {
        var services = BuildCompatibleServices(failureThreshold: 1);
        using var serviceProvider = services.BuildServiceProvider();
        var pipeline = serviceProvider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(LlmProviderRegistration.OpenAiCompatibleHttpClientName);
        var dispatchHandler = EnumeratePipeline(pipeline)
            .OfType<DelegatingHandler>()
            .Single(handler => handler.GetType().Name == "LlmDispatchTrackingHandler");
        var transport = new SequentialResponseHandler(
            new HttpResponseMessage(HttpStatusCode.NotImplemented),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"fallback reply"},"finish_reason":"stop"}],"usage":{"total_tokens":9}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        dispatchHandler.InnerHandler = transport;
        using var scope = serviceProvider.CreateScope();
        var compatibleProvider = scope.ServiceProvider.GetRequiredService<ILlmProvider>();

        var events = new List<LlmTokenEvent>();
        await foreach (var item in compatibleProvider.StreamAsync(new ChatCompletionRequest(
                           [new ChatCompletionMessage("User", "fallback")],
                           SystemPrompt: string.Empty)))
        {
            events.Add(item);
        }

        events.Should().ContainSingle();
        events[0].Token.Should().Be("fallback reply");
        events[0].IsDegraded.Should().BeTrue();
        events[0].Error.Should().BeNull();
        transport.InvocationCount.Should().Be(2);
        serviceProvider.GetRequiredService<CircuitBreakerStateTracker>()
            .Get("OpenAICompatible")?.State.Should().NotBe(CircuitState.Open);
    }

    [Fact]
    public async Task CompatibleRegisteredPolicy_PreDispatchPollyRejection_DoesNotPoisonCompanionCircuit()
    {
        var services = BuildCompatibleServices(failureThreshold: 1);
        using var serviceProvider = services.BuildServiceProvider();
        var pipeline = serviceProvider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(LlmProviderRegistration.OpenAiCompatibleHttpClientName);
        var dispatchHandler = EnumeratePipeline(pipeline)
            .OfType<DelegatingHandler>()
            .Single(handler => handler.GetType().Name == "LlmDispatchTrackingHandler");
        var blockingBody = new SignallingBlockingReadStream();
        var transport = new SequentialResponseHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StreamContent(blockingBody)
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"unexpected transport response"},"finish_reason":"stop"}],"usage":{"total_tokens":9}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        dispatchHandler.InnerHandler = transport;
        using var scope = serviceProvider.CreateScope();
        var compatibleProvider = scope.ServiceProvider.GetRequiredService<ILlmProvider>();
        using var cancellation = new CancellationTokenSource();
        var firstRequest = compatibleProvider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "first request")],
            SystemPrompt: string.Empty), cancellation.Token);
        await blockingBody.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRequest);
        var secondResult = await compatibleProvider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "second request")],
            SystemPrompt: string.Empty));

        secondResult.IsDegraded.Should().BeTrue();
        transport.InvocationCount.Should().Be(1,
            "the open Polly circuit must reject the second request before dispatch");
        var tracker = serviceProvider.GetRequiredService<CircuitBreakerStateTracker>();
        tracker.RecordState("OpenAICompatible", CircuitState.Closed);
        tracker.Get("OpenAICompatible")?.State.Should().Be(CircuitState.Closed,
            "a pre-dispatch Polly rejection must not open the companion provider circuit");
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

    private static IConfigurationRoot BuildRealProviderConfiguration(string? providerOverride = null)
    {
        var prefix = $"TASKDECK_TEST_{Guid.NewGuid():N}_";
        var providerVariable = $"{prefix}Llm__Provider";
        var retiredChildVariable = $"{prefix}Llm__Gemini__ApiKey";
        try
        {
            if (providerOverride is not null)
            {
                Environment.SetEnvironmentVariable(providerVariable, providerOverride);
            }

            Environment.SetEnvironmentVariable(retiredChildVariable, "stale-test-key");
            return new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddEnvironmentVariables(prefix)
                .Build();
        }
        finally
        {
            Environment.SetEnvironmentVariable(providerVariable, null);
            Environment.SetEnvironmentVariable(retiredChildVariable, null);
        }
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
                ["Llm:OpenAiCompatible:ApiKey"] = "test-compatible-key",
                ["Llm:OpenAiCompatible:BaseUrl"] = providerName == "OpenAiCompatible" && providerBaseUrl is not null
                    ? providerBaseUrl
                    : environmentName == "Production"
                        ? "https://api.groq.com/openai/v1"
                        : "http://localhost:12345/openai/v1",
                ["Llm:OpenAiCompatible:Model"] = "test-compatible-model",
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
        "OpenAiCompatible" =>
            """
            {"choices":[{"message":{"content":"OK"},"finish_reason":"stop"}],"usage":{"total_tokens":1}}
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
                // The OpenAI adapter sends the current `max_completion_tokens`
                // parameter; only OpenAiCompatible still speaks `max_tokens`.
                // `test-openai-model` is non-reasoning, so no headroom is added
                // and temperature is still sent.
                root.GetProperty("max_completion_tokens").GetInt32().Should().Be(expectedMaxTokens);
                root.TryGetProperty("max_tokens", out _).Should().BeFalse();
                root.GetProperty("temperature").GetDouble().Should().BeApproximately(expectedTemperature, 0.000001);
                root.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be(expectedContent);
                break;
            case "OpenAiCompatible":
                root.GetProperty("model").GetString().Should().Be("test-compatible-model");
                root.GetProperty("stream").GetBoolean().Should().BeFalse();
                root.GetProperty("max_tokens").GetInt32().Should().Be(expectedMaxTokens);
                root.GetProperty("temperature").GetDouble().Should().BeApproximately(expectedTemperature, 0.000001);
                root.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be(expectedContent);
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

    private static ServiceCollection BuildCompatibleServices(int failureThreshold = 5)
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
                ["CircuitBreaker:FailureThreshold"] = failureThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["CircuitBreaker:BreakDurationSeconds"] = "60"
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

    private sealed class SequentialResponseHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public int InvocationCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class SignallingBlockingReadStream : Stream
    {
        private readonly TaskCompletionSource _readStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static IEnumerable<HttpMessageHandler> EnumeratePipeline(HttpMessageHandler root)
    {
        for (var current = root; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
            yield return current;
    }
}
