using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OllamaLlmProviderTests
{
    // -----------------------------------------------------------------------
    // TryParseResponse — static parsing of Ollama /api/chat responses
    // -----------------------------------------------------------------------

    [Fact]
    public void TryParseResponse_ShouldReturnTrue_WhenResponseIsWellFormed()
    {
        const string body = """
            {"message":{"content":"Hello from Ollama"},"done":true,"eval_count":10,"done_reason":"stop"}
            """;

        var result = OllamaLlmProvider.TryParseResponse(body, out var content, out var tokensUsed, out var doneReason);

        result.Should().BeTrue();
        content.Should().Be("Hello from Ollama");
        tokensUsed.Should().Be(10);
        doneReason.Should().Be("stop");
    }

    [Fact]
    public void TryParseResponse_ShouldFallBackToPromptEvalCount_WhenEvalCountIsMissing()
    {
        const string body = """
            {"message":{"content":"Hello"},"done":true,"prompt_eval_count":7}
            """;

        var result = OllamaLlmProvider.TryParseResponse(body, out _, out var tokensUsed, out _);

        result.Should().BeTrue();
        tokensUsed.Should().Be(7);
    }

    [Fact]
    public void TryParseResponse_ShouldSumPromptAndCompletionTokens_WhenBothCountsArePresent()
    {
        const string body = """
            {"message":{"content":"Hello"},"done":true,"prompt_eval_count":7,"eval_count":11}
            """;

        var result = OllamaLlmProvider.TryParseResponse(body, out _, out var tokensUsed, out _);

        result.Should().BeTrue();
        tokensUsed.Should().Be(18);
    }

    [Fact]
    public void TryParseResponse_ShouldEstimateTokens_WhenNeitherEvalCountFieldIsPresent()
    {
        const string body = """
            {"message":{"content":"Hello world"},"done":true}
            """;

        var result = OllamaLlmProvider.TryParseResponse(body, out var content, out var tokensUsed, out _);

        result.Should().BeTrue();
        content.Should().Be("Hello world");
        tokensUsed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryParseResponse_ShouldReturnNullDoneReason_WhenDoneReasonIsAbsent()
    {
        const string body = """
            {"message":{"content":"Response text"},"done":true,"eval_count":5}
            """;

        var result = OllamaLlmProvider.TryParseResponse(body, out _, out _, out var doneReason);

        result.Should().BeTrue();
        doneReason.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void TryParseResponse_ShouldReturnFalse_WhenInputIsNullOrWhiteSpace(string? input)
    {
        var result = OllamaLlmProvider.TryParseResponse(input!, out var content, out var tokensUsed, out var doneReason);

        result.Should().BeFalse();
        content.Should().BeEmpty();
        tokensUsed.Should().Be(0);
        doneReason.Should().BeNull();
    }

    [Fact]
    public void TryParseResponse_ShouldReturnFalse_WhenBodyIsMalformedJson()
    {
        const string body = "{not valid json";

        var result = OllamaLlmProvider.TryParseResponse(body, out var content, out var tokensUsed, out _);

        result.Should().BeFalse();
        content.Should().BeEmpty();
        tokensUsed.Should().Be(0);
    }

    [Fact]
    public void TryParseResponse_ShouldReturnFalse_WhenMessagePropertyIsMissing()
    {
        const string body = """{"done":true,"eval_count":5}""";

        var result = OllamaLlmProvider.TryParseResponse(body, out _, out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseResponse_ShouldReturnFalse_WhenContentPropertyIsMissing()
    {
        const string body = """{"message":{"role":"assistant"},"done":true}""";

        var result = OllamaLlmProvider.TryParseResponse(body, out _, out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseResponse_ShouldReturnFalse_WhenContentIsEmptyString()
    {
        const string body = """{"message":{"content":""},"done":true,"eval_count":3}""";

        var result = OllamaLlmProvider.TryParseResponse(body, out _, out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseResponse_ShouldReturnFalse_WhenContentIsWhitespaceOnly()
    {
        const string body = """{"message":{"content":"   "},"done":true,"eval_count":3}""";

        var result = OllamaLlmProvider.TryParseResponse(body, out _, out _, out _);

        result.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // LooksLikeTruncatedJson — shared static, same contract as OpenAI/Gemini
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("{\"reply\":\"incomplete", true)]
    [InlineData("{}", false)]
    [InlineData("plain text response", false)]
    [InlineData("", false)]
    [InlineData("  { broken json", true)]
    [InlineData("[{\"id\":1},", true)]
    [InlineData("[\"complete\"]", false)]
    [InlineData("  [ broken array", true)]
    public void LooksLikeTruncatedJson_ShouldDetectPartialJson(string input, bool expected)
    {
        OllamaLlmProvider.LooksLikeTruncatedJson(input).Should().Be(expected);
    }

    [Fact]
    public void LooksLikeTruncatedJson_ShouldReturnFalse_WhenInputIsWhitespaceOnly()
    {
        OllamaLlmProvider.LooksLikeTruncatedJson("   ").Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // CompleteAsync — HttpClient-level integration via StubHttpMessageHandler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CompleteAsync_ShouldReturnParsedCompletion_WhenOllamaResponseIsValid()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"message":{"content":"create card for provider runtime"},"done":true,"eval_count":15,"done_reason":"stop"}
                    """,
                    Encoding.UTF8,
                    "application/json")
            });

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card for provider runtime")
            }));

        result.Content.Should().Contain("create card for provider runtime");
        result.TokensUsed.Should().Be(15);
        result.Provider.Should().Be("Ollama");
        result.Model.Should().Be(settings.Ollama!.Model);
        result.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_ShouldFallback_WhenOllamaResponseIsFailure()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway));

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("request failed");
        result.Provider.Should().Be("Ollama");
        result.Model.Should().Be(settings.Ollama!.Model);
        result.IsDegraded.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteAsync_ShouldFallback_WhenOllamaResponseIsInvalid()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("response parsing failed");
        result.Provider.Should().Be("Ollama");
        result.IsDegraded.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteAsync_ShouldFallback_WhenConfigurationIsInvalid()
    {
        var settings = BuildSettings();
        settings.Ollama!.Model = string.Empty;

        var provider = new OllamaLlmProvider(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("configuration is invalid");
        result.Provider.Should().Be("Ollama");
        result.IsDegraded.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteAsync_ShouldReturnDegraded_WhenDoneReasonIsLength()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"message":{"content":"partial response"},"done":true,"eval_count":50,"done_reason":"length"}
                    """,
                    Encoding.UTF8,
                    "application/json")
            });

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "tell me something")],
            SystemPrompt: string.Empty));

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Be("Response was truncated");
        result.Content.Should().Be("partial response");
        result.IsActionable.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_ShouldReturnDegraded_WhenJsonModeResponseIsInvalidJson()
    {
        var settings = BuildSettings();
        // The inner content is truncated JSON — properly escaped so the Ollama envelope is valid.
        var truncatedContent = "{\\\"reply\\\":\\\"this is cut off";
        var responseBody = $@"{{""message"":{{""content"":""{truncatedContent}""}},""done"":true,""eval_count"":50,""done_reason"":""stop""}}";

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        // SystemPrompt defaults to null → JSON mode is requested
        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "tell me something")]));

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Be("Response was truncated");
    }

    [Fact]
    public async Task CompleteAsync_ShouldThrowOperationCanceled_WhenRequestIsCancelled()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var act = async () => await provider.CompleteAsync(
            new ChatCompletionRequest(new List<ChatCompletionMessage> { new("User", "hello") }),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CompleteAsync_ShouldRedactSensitiveDetails_WhenUnexpectedExceptionIsLogged()
    {
        var settings = BuildSettings();
        var logger = new InMemoryLogger<OllamaLlmProvider>();
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException(
                "Authorization: Bearer ollama-secret {\"text\":\"capture secret\"} token=local-token"));

        var provider = new OllamaLlmProvider(new HttpClient(handler), settings, logger);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("request errored");
        logger.Entries.Should().ContainSingle(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        var message = logger.Entries.Single(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error).Message;
        message.Should().Contain("Ollama completion request failed with unexpected error.");
        message.Should().NotContain("ollama-secret");
        message.Should().NotContain("capture secret");
        message.Should().NotContain("local-token");
        message.Should().Contain($"Authorization: Bearer {SensitiveDataRedactor.RedactedValue}");
    }

    [Fact]
    public async Task CompleteAsync_ShouldAddAttributionHeaders_WhenAttributionIsProvided()
    {
        var settings = BuildSettings();
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        string? correlationHeader = null;
        string? sourceSurfaceHeader = null;
        string? userTokenHeader = null;
        string? boardTokenHeader = null;
        string? sessionTokenHeader = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.CorrelationHeader, out var correlationValues))
                correlationHeader = correlationValues.SingleOrDefault();
            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.SourceSurfaceHeader, out var sourceSurfaceValues))
                sourceSurfaceHeader = sourceSurfaceValues.SingleOrDefault();
            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.UserTokenHeader, out var userTokenValues))
                userTokenHeader = userTokenValues.SingleOrDefault();
            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.BoardTokenHeader, out var boardTokenValues))
                boardTokenHeader = boardTokenValues.SingleOrDefault();
            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.SessionTokenHeader, out var sessionTokenValues))
                sessionTokenHeader = sessionTokenValues.SingleOrDefault();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"message":{"content":"ok"},"done":true,"eval_count":3,"done_reason":"stop"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "hello") },
            Attribution: new LlmRequestAttribution(
                userId,
                "req-ollama-attribution",
                LlmRequestSourceSurface.Chat,
                boardId,
                sessionId)));

        correlationHeader.Should().Be("req-ollama-attribution");
        sourceSurfaceHeader.Should().Be("chat");
        userTokenHeader.Should().StartWith("usr_");
        userTokenHeader.Should().NotContain(userId.ToString("N"), "provider attribution should be pseudonymous");
        boardTokenHeader.Should().StartWith("brd_");
        sessionTokenHeader.Should().StartWith("ses_");
    }

    [Fact]
    public async Task GetHealthAsync_ShouldReportAvailableAndModel_WhenConfigurationIsValid()
    {
        var settings = BuildSettings();
        var provider = new OllamaLlmProvider(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var health = await provider.GetHealthAsync();

        health.IsAvailable.Should().BeTrue();
        health.ProviderName.Should().Be("Ollama");
        health.Model.Should().Be(settings.Ollama!.Model);
    }

    [Fact]
    public async Task GetHealthAsync_ShouldReportUnavailable_WhenConfigurationIsInvalid()
    {
        var settings = BuildSettings();
        settings.Ollama!.Model = string.Empty;

        var provider = new OllamaLlmProvider(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var health = await provider.GetHealthAsync();

        health.IsAvailable.Should().BeFalse();
        health.ProviderName.Should().Be("Ollama");
        health.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StreamAsync_ShouldEmitWordTokens_AndMarkLastTokenAsComplete()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"message":{"content":"alpha beta"},"done":true,"eval_count":7,"done_reason":"stop"}""",
                    Encoding.UTF8,
                    "application/json")
            });

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var events = new List<LlmTokenEvent>();
        await foreach (var tokenEvent in provider.StreamAsync(
            new ChatCompletionRequest(new List<ChatCompletionMessage> { new("User", "hello") })))
        {
            events.Add(tokenEvent);
        }

        events.Should().HaveCount(2);
        events[0].Token.Should().Be("alpha");
        events[0].IsComplete.Should().BeFalse();
        events[0].TokensUsed.Should().BeNull();
        events[0].Provider.Should().BeNull();
        events[1].Token.Should().Be(" beta");
        events[1].IsComplete.Should().BeTrue();
        events[1].TokensUsed.Should().Be(7);
        events[1].Provider.Should().Be("Ollama");
        events[1].Model.Should().NotBeNullOrWhiteSpace();
    }

    // -----------------------------------------------------------------------
    // LlmProviderSelectionPolicy — Ollama-specific selection and validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Evaluate_ShouldSelectOllama_WhenProviderIsOllamaAndSettingsAreValid()
    {
        var settings = BuildPolicySettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "Ollama";

        // Development + AllowLiveProvidersInDevelopment: localhost is permitted for local LLM gateways
        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Ollama);
        result.Reason.Should().Contain("Ollama provider selected");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenDevelopmentOllamaLocalhostIsNotExplicitlyAllowed()
    {
        var settings = BuildPolicySettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "Ollama";
        settings.Ollama!.AllowLocalhostEndpoints = false;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("Ollama configuration is invalid");
        result.Reason.Should().Contain("SSRF");
    }

    [Fact]
    public void Evaluate_ShouldSelectOllama_WhenProviderIsOllamaInProductionWithPublicUrl()
    {
        var settings = BuildPolicySettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "Ollama";
        settings.Ollama!.BaseUrl = "https://ollama.mycompany.com";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Ollama);
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenProviderIsOllamaAndModelIsEmpty()
    {
        var settings = BuildPolicySettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "Ollama";
        settings.Ollama!.Model = string.Empty;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("Ollama configuration is invalid");
        result.Reason.Should().Contain("Model is required");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenProviderIsOllamaAndBaseUrlIsInvalid()
    {
        var settings = BuildPolicySettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "Ollama";
        settings.Ollama!.BaseUrl = "not-a-url";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("Ollama configuration is invalid");
        result.Reason.Should().Contain("BaseUrl must be an absolute HTTP(S) URI");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenProviderIsOllamaAndSettingsAreMissing()
    {
        var settings = BuildPolicySettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "Ollama";
        settings.Ollama = null!;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("Ollama settings are required");
    }

    [Fact]
    public void TryValidateOllamaSettings_ShouldReturnTrue_WhenSettingsAreValid()
    {
        var settings = BuildPolicySettings();

        var isValid = LlmProviderSelectionPolicy.TryValidateOllamaSettings(
            settings, out var error, allowLocalhostEndpoints: true);

        isValid.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryValidateOllamaSettings_ShouldReturnFalse_WhenOllamaSectionIsNull()
    {
        var settings = BuildPolicySettings();
        settings.Ollama = null!;

        var isValid = LlmProviderSelectionPolicy.TryValidateOllamaSettings(settings, out var error);

        isValid.Should().BeFalse();
        error.Should().Contain("Ollama settings are required");
    }

    [Fact]
    public void TryValidateOllamaSettings_ShouldReturnFalse_WhenModelIsEmpty()
    {
        var settings = BuildPolicySettings();
        settings.Ollama!.Model = string.Empty;

        var isValid = LlmProviderSelectionPolicy.TryValidateOllamaSettings(
            settings, out var error, allowLocalhostEndpoints: true);

        isValid.Should().BeFalse();
        error.Should().Contain("Model is required");
    }

    [Fact]
    public void TryValidateOllamaSettings_ShouldReturnFalse_WhenBaseUrlIsNotAbsolute()
    {
        var settings = BuildPolicySettings();
        settings.Ollama!.BaseUrl = "relative/path";

        var isValid = LlmProviderSelectionPolicy.TryValidateOllamaSettings(
            settings, out var error, allowLocalhostEndpoints: true);

        isValid.Should().BeFalse();
        error.Should().Contain("BaseUrl must be an absolute HTTP(S) URI");
    }

    [Fact]
    public void TryValidateOllamaSettings_ShouldReturnFalse_WhenTimeoutIsZero()
    {
        var settings = BuildPolicySettings();
        settings.Ollama!.TimeoutSeconds = 0;

        var isValid = LlmProviderSelectionPolicy.TryValidateOllamaSettings(
            settings, out var error, allowLocalhostEndpoints: true);

        isValid.Should().BeFalse();
        error.Should().Contain("TimeoutSeconds must be greater than zero");
    }

    [Theory]
    [InlineData("https://10.0.0.1/api")]
    [InlineData("https://192.168.1.1/api")]
    [InlineData("https://172.16.0.1/api")]
    public void TryValidateOllamaSettings_ShouldReturnFalse_WhenBaseUrlTargetsPrivateIp(string baseUrl)
    {
        var settings = BuildPolicySettings();
        settings.Ollama!.BaseUrl = baseUrl;

        var isValid = LlmProviderSelectionPolicy.TryValidateOllamaSettings(settings, out var error);

        isValid.Should().BeFalse();
        error.Should().Contain("SSRF");
    }

    [Fact]
    public void TryValidateOllamaSettings_ShouldAcceptLocalhost_WhenLocalhostIsAllowed()
    {
        var settings = BuildPolicySettings();
        // default BaseUrl is http://localhost:11434

        var isValid = LlmProviderSelectionPolicy.TryValidateOllamaSettings(
            settings, out _, allowLocalhostEndpoints: true);

        isValid.Should().BeTrue("localhost should be accepted when allowLocalhostEndpoints is true");
    }

    [Fact]
    public void TryValidateOllamaSettings_ShouldRejectLocalhost_WhenLocalhostIsNotAllowed()
    {
        var settings = BuildPolicySettings();
        // default BaseUrl is http://localhost:11434

        var isValid = LlmProviderSelectionPolicy.TryValidateOllamaSettings(
            settings, out var error, allowLocalhostEndpoints: false);

        isValid.Should().BeFalse("localhost should be rejected when allowLocalhostEndpoints is false");
        error.Should().Contain("SSRF");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static LlmProviderSettings BuildSettings()
    {
        return new LlmProviderSettings
        {
            EnableLiveProviders = true,
            AllowLiveProvidersInDevelopment = true,
            Provider = "Ollama",
            Ollama = new OllamaProviderSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "llama3.2",
                TimeoutSeconds = 120,
                AllowLocalhostEndpoints = true
            }
        };
    }

    /// <summary>
    /// Settings used for selection-policy tests. The Ollama BaseUrl defaults to
    /// http://localhost:11434, which requires <c>allowLocalhostEndpoints: true</c>
    /// or a Development environment with AllowLiveProvidersInDevelopment to pass validation.
    /// </summary>
    private static LlmProviderSettings BuildPolicySettings()
    {
        return new LlmProviderSettings
        {
            EnableLiveProviders = true,
            AllowLiveProvidersInDevelopment = true,
            Provider = "Ollama",
            OpenAi = new OpenAiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                TimeoutSeconds = 30
            },
            Gemini = new GeminiProviderSettings
            {
                ApiKey = "test-gemini-key",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                Model = "gemini-2.5-flash",
                TimeoutSeconds = 30
            },
            Ollama = new OllamaProviderSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "llama3.2",
                TimeoutSeconds = 120,
                AllowLocalhostEndpoints = true
            }
        };
    }

    // -----------------------------------------------------------------------
    // ProbeAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_ShouldReturnHealthy_WhenOllamaRespondsSuccessfully()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"message":{"content":"OK"},"done":true,"eval_count":1,"done_reason":"stop"}""",
                    Encoding.UTF8,
                    "application/json")
            });

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var health = await provider.ProbeAsync();

        health.IsAvailable.Should().BeTrue();
        health.ProviderName.Should().Be("Ollama");
        health.Model.Should().Be("llama3.2");
        health.IsProbed.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAsync_ShouldReturnUnhealthy_WhenOllamaReturnsError()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var health = await provider.ProbeAsync();

        health.IsAvailable.Should().BeFalse();
        health.ProviderName.Should().Be("Ollama");
        health.IsProbed.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAsync_ShouldReturnUnhealthy_WhenSettingsAreInvalid()
    {
        var settings = BuildSettings();
        settings.Ollama!.Model = "";

        var provider = new OllamaLlmProvider(
            new HttpClient(),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var health = await provider.ProbeAsync();

        health.IsAvailable.Should().BeFalse();
        health.ProviderName.Should().Be("Ollama");
        health.IsProbed.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAsync_ShouldReturnUnhealthy_WhenHttpThrows()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("Connection refused"));

        var provider = new OllamaLlmProvider(
            new HttpClient(handler),
            settings,
            NullLogger<OllamaLlmProvider>.Instance);

        var health = await provider.ProbeAsync();

        health.IsAvailable.Should().BeFalse();
        health.ProviderName.Should().Be("Ollama");
        health.IsProbed.Should().BeTrue();
        health.ErrorMessage.Should().Contain("errored");
    }
}
