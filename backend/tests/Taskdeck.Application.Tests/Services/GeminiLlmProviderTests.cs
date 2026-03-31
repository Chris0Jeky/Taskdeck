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

public class GeminiLlmProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldReturnParsedCompletion_WhenGeminiResponseIsValid()
    {
        var settings = BuildSettings();
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var requestUri = string.Empty;
        string[] headerValues = Array.Empty<string>();
        string? correlationHeader = null;
        string? sourceSurfaceHeader = null;
        string? userTokenHeader = null;
        string? boardTokenHeader = null;
        string? sessionTokenHeader = null;
        var requestRoles = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestUri = request.RequestUri?.ToString() ?? string.Empty;
            if (request.Headers.TryGetValues("x-goog-api-key", out var headers))
            {
                headerValues = headers.ToArray();
            }

            var body = request.Content is null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var json = System.Text.Json.JsonDocument.Parse(body);
            var contents = json.RootElement.GetProperty("contents");
            foreach (var content in contents.EnumerateArray())
            {
                requestRoles.Add(content.GetProperty("role").GetString() ?? string.Empty);
            }

            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.CorrelationHeader, out var correlationValues))
            {
                correlationHeader = correlationValues.SingleOrDefault();
            }

            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.SourceSurfaceHeader, out var sourceSurfaceValues))
            {
                sourceSurfaceHeader = sourceSurfaceValues.SingleOrDefault();
            }

            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.UserTokenHeader, out var userTokenValues))
            {
                userTokenHeader = userTokenValues.SingleOrDefault();
            }

            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.BoardTokenHeader, out var boardTokenValues))
            {
                boardTokenHeader = boardTokenValues.SingleOrDefault();
            }

            if (request.Headers.TryGetValues(LlmRequestAttributionMapper.SessionTokenHeader, out var sessionTokenValues))
            {
                sessionTokenHeader = sessionTokenValues.SingleOrDefault();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [
                              { "text": "Ship provider parity" },
                              { "text": "with tests" }
                            ]
                          }
                        }
                      ],
                      "usageMetadata": {
                        "totalTokenCount": 33
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("System", "Follow board-safe constraints."),
                new("Assistant", "Ready."),
                new("User", "create card for provider runtime")
            },
            Attribution: new LlmRequestAttribution(
                userId,
                "req-gemini-attribution",
                LlmRequestSourceSurface.Chat,
                boardId,
                sessionId)));

        result.Content.Should().Contain("Ship provider parity");
        result.Content.Should().Contain("with tests");
        result.TokensUsed.Should().Be(33);
        result.IsActionable.Should().BeTrue();
        result.ActionIntent.Should().Be("card.create");
        result.Provider.Should().Be("Gemini");
        result.Model.Should().Be(settings.Gemini.Model);
        requestUri.Should().NotContain("?key=");
        requestRoles.Should().ContainInOrder("user", "model", "user");
        headerValues.Should().ContainSingle().Which.Should().Be(settings.Gemini.ApiKey);
        correlationHeader.Should().Be("req-gemini-attribution");
        sourceSurfaceHeader.Should().Be("chat");
        userTokenHeader.Should().StartWith("usr_");
        userTokenHeader.Should().NotContain(userId.ToString("N"), "provider attribution should be pseudonymous");
        boardTokenHeader.Should().StartWith("brd_");
        sessionTokenHeader.Should().StartWith("ses_");
    }

    [Fact]
    public async Task CompleteAsync_ShouldFallback_WhenGeminiResponseIsFailure()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway));

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("request failed");
        result.IsActionable.Should().BeTrue();
        result.ActionIntent.Should().Be("card.create");
        result.Provider.Should().Be("Gemini");
        result.Model.Should().Be(settings.Gemini.Model);
    }

    [Fact]
    public async Task CompleteAsync_ShouldFallback_WhenGeminiResponseIsInvalid()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("response parsing failed");
        result.Provider.Should().Be("Gemini");
        result.Model.Should().Be(settings.Gemini.Model);
    }

    [Fact]
    public async Task CompleteAsync_ShouldFallback_WhenConfigurationIsInvalid()
    {
        var settings = BuildSettings();
        settings.Gemini.ApiKey = string.Empty;
        var provider = new GeminiLlmProvider(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            settings,
            NullLogger<GeminiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("configuration is invalid");
        result.Provider.Should().Be("Gemini");
        result.Model.Should().Be(settings.Gemini.Model);
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

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var act = async () => await provider.CompleteAsync(
            new ChatCompletionRequest(new List<ChatCompletionMessage> { new("User", "hello") }),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetHealthAsync_ShouldReportAvailabilityAndModel_WhenConfigurationIsValid()
    {
        var settings = BuildSettings();
        var provider = new GeminiLlmProvider(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            settings,
            NullLogger<GeminiLlmProvider>.Instance);

        var health = await provider.GetHealthAsync();

        health.IsAvailable.Should().BeTrue();
        health.ProviderName.Should().Be("Gemini");
        health.Model.Should().Be(settings.Gemini.Model);
    }

    [Fact]
    public async Task GetHealthAsync_ShouldReportUnavailable_WhenConfigurationIsInvalid()
    {
        var settings = BuildSettings();
        settings.Gemini.ApiKey = string.Empty;
        var provider = new GeminiLlmProvider(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            settings,
            NullLogger<GeminiLlmProvider>.Instance);

        var health = await provider.GetHealthAsync();

        health.IsAvailable.Should().BeFalse();
        health.ProviderName.Should().Be("Gemini");
        health.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CompleteAsync_ShouldDefaultNullRoleToUser_WhenMappingMessages()
    {
        var settings = BuildSettings();
        var capturedRoles = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var body = request.Content is null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var json = System.Text.Json.JsonDocument.Parse(body);
            var contents = json.RootElement.GetProperty("contents");
            foreach (var content in contents.EnumerateArray())
            {
                capturedRoles.Add(content.GetProperty("role").GetString() ?? string.Empty);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [
                              { "text": "ok" }
                            ]
                          }
                        }
                      ],
                      "usageMetadata": {
                        "totalTokenCount": 3
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);

        var _ = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new(null!, "raw prompt")
            }));

        // System prompt is now sent via system_instruction, not as a user message in contents
        capturedRoles.Should().ContainSingle().Which.Should().Be("user");
    }

    [Fact]
    public async Task CompleteAsync_ShouldRedactSensitiveDetails_WhenUnexpectedExceptionIsLogged()
    {
        var settings = BuildSettings();
        var logger = new InMemoryLogger<GeminiLlmProvider>();
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException(
                "x-goog-api-key: gemini-secret {\"text\":\"capture secret\"} token=provider-token"));

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, logger);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("request errored");
        logger.Entries.Should().ContainSingle(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        var message = logger.Entries.Single(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error).Message;
        message.Should().Contain("Gemini completion request failed with unexpected error.");
        message.Should().NotContain("gemini-secret");
        message.Should().NotContain("capture secret");
        message.Should().NotContain("provider-token");
        message.Should().Contain($"x-goog-api-key: {SensitiveDataRedactor.RedactedValue}");
    }

    [Fact]
    public async Task CompleteAsync_ShouldSendSystemPromptAsSystemInstruction_NotAsUserMessage()
    {
        var settings = BuildSettings();
        string? capturedSystemInstruction = null;
        var capturedContentRoles = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var body = request.Content is null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var json = System.Text.Json.JsonDocument.Parse(body);
            var root = json.RootElement;

            if (root.TryGetProperty("system_instruction", out var sysInstr) &&
                sysInstr.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0)
            {
                capturedSystemInstruction = parts[0].GetProperty("text").GetString();
            }

            var contents = root.GetProperty("contents");
            foreach (var content in contents.EnumerateArray())
            {
                capturedContentRoles.Add(content.GetProperty("role").GetString() ?? string.Empty);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [{ "text": "ok" }]
                          }
                        }
                      ],
                      "usageMetadata": { "totalTokenCount": 5 }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);

        await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card 'Test'")
            }));

        capturedSystemInstruction.Should().NotBeNullOrWhiteSpace("system prompt should be sent via system_instruction");
        capturedSystemInstruction.Should().Contain("Taskdeck");
        // The contents array should NOT contain the system prompt as a user message
        capturedContentRoles.Should().ContainSingle().Which.Should().Be("user");
    }

    [Fact]
    public async Task CompleteAsync_ShouldOmitSystemInstruction_WhenSystemPromptIsEmpty()
    {
        var settings = BuildSettings();
        var hasSystemInstruction = false;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var body = request.Content is null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var json = System.Text.Json.JsonDocument.Parse(body);
            hasSystemInstruction = json.RootElement.TryGetProperty("system_instruction", out _);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [{ "text": "OK" }]
                          }
                        }
                      ],
                      "usageMetadata": { "totalTokenCount": 2 }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);

        // Probes pass SystemPrompt: string.Empty to opt out
        await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "Reply with exactly: OK") },
            SystemPrompt: string.Empty));

        hasSystemInstruction.Should().BeFalse("empty system prompt should not produce system_instruction field");
    }

    [Fact]
    public async Task CompleteAsync_ShouldReturnDegraded_WhenFinishReasonIsMaxTokens()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [{ "text": "partial response" }]
                          },
                          "finishReason": "MAX_TOKENS"
                        }
                      ],
                      "usageMetadata": { "totalTokenCount": 50 }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);
        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "tell me something")],
            SystemPrompt: string.Empty));

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Be("Response was truncated");
        result.Content.Should().Be("partial response");
        result.IsActionable.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"reply\":\"incomplete", true)]
    [InlineData("{}", false)]
    [InlineData("plain text response", false)]
    [InlineData("", false)]
    [InlineData("  { broken json", true)]
    public void LooksLikeTruncatedJson_ShouldDetectPartialJson(string input, bool expected)
    {
        GeminiLlmProvider.LooksLikeTruncatedJson(input).Should().Be(expected);
    }

    private static LlmProviderSettings BuildSettings()
    {
        return new LlmProviderSettings
        {
            EnableLiveProviders = true,
            Provider = "Gemini",
            Gemini = new GeminiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                Model = "gemini-2.5-flash",
                TimeoutSeconds = 30
            }
        };
    }

}
