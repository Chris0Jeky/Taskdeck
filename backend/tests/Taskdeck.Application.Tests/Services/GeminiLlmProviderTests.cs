using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class GeminiLlmProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldReturnParsedCompletion_WhenGeminiResponseIsValid()
    {
        var settings = BuildSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
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
            });

        var provider = new GeminiLlmProvider(new HttpClient(handler), settings, NullLogger<GeminiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("User", "create card for provider runtime")
            }));

        result.Content.Should().Contain("Ship provider parity");
        result.Content.Should().Contain("with tests");
        result.TokensUsed.Should().Be(33);
        result.IsActionable.Should().BeTrue();
        result.ActionIntent.Should().Be("card.create");
        result.Provider.Should().Be("Gemini");
        result.Model.Should().Be(settings.Gemini.Model);
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

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = (request, _) => Task.FromResult(responseFactory(request));
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await _responseFactory(request, cancellationToken);
        }
    }
}
