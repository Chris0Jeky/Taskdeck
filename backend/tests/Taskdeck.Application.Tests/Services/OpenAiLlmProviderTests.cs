using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OpenAiLlmProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldReturnParsedCompletion_WhenOpenAiResponseIsValid()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "Use lane-based decomposition to implement this task."
                          }
                        }
                      ],
                      "usage": {
                        "total_tokens": 42
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        var httpClient = new HttpClient(handler);
        var provider = new OpenAiLlmProvider(httpClient, settings, NullLogger<OpenAiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card for llm provider setup")
            }));

        result.Content.Should().Contain("lane-based decomposition");
        result.TokensUsed.Should().Be(42);
        result.IsActionable.Should().BeTrue();
        result.ActionIntent.Should().Be("card.create");
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be(settings.OpenAi.Model);
    }

    [Fact]
    public async Task CompleteAsync_ShouldFallback_WhenOpenAiResponseIsFailure()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway));
        var httpClient = new HttpClient(handler);
        var provider = new OpenAiLlmProvider(httpClient, settings, NullLogger<OpenAiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card from this instruction")
            }));

        result.Content.Should().Contain("request failed");
        result.IsActionable.Should().BeTrue();
        result.ActionIntent.Should().Be("card.create");
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be(settings.OpenAi.Model);
    }

    [Fact]
    public async Task GetHealthAsync_ShouldReportUnavailable_WhenConfigurationIsInvalid()
    {
        var settings = BuildSettings();
        settings.OpenAi.ApiKey = string.Empty;
        var provider = new OpenAiLlmProvider(new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var health = await provider.GetHealthAsync();

        health.IsAvailable.Should().BeFalse();
        health.ProviderName.Should().Be("OpenAI");
        health.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        health.Model.Should().Be(settings.OpenAi.Model);
    }

    [Fact]
    public async Task StreamAsync_ShouldEmitWordTokens_AndMarkLastTokenAsComplete()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "alpha beta"
                          }
                        }
                      ],
                      "usage": {
                        "total_tokens": 7
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        var provider = new OpenAiLlmProvider(new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var events = new List<LlmTokenEvent>();
        await foreach (var tokenEvent in provider.StreamAsync(new ChatCompletionRequest(new List<ChatCompletionMessage> { new("User", "hello") })))
        {
            events.Add(tokenEvent);
        }

        events.Should().HaveCount(2);
        events[0].Token.Should().Be("alpha");
        events[0].IsComplete.Should().BeFalse();
        events[1].Token.Should().Be(" beta");
        events[1].IsComplete.Should().BeTrue();
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
            using var json = JsonDocument.Parse(body);
            var messages = json.RootElement.GetProperty("messages");
            foreach (var message in messages.EnumerateArray())
            {
                capturedRoles.Add(message.GetProperty("role").GetString() ?? string.Empty);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "ok"
                          }
                        }
                      ],
                      "usage": {
                        "total_tokens": 3
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var provider = new OpenAiLlmProvider(new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var _ = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new(null!, "raw prompt")
            }));

        capturedRoles.Should().ContainSingle().Which.Should().Be("user");
    }

    private static LlmProviderSettings BuildSettings()
    {
        return new LlmProviderSettings
        {
            EnableLiveProviders = true,
            Provider = "OpenAI",
            OpenAi = new OpenAiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                TimeoutSeconds = 30
            }
        };
    }

}
