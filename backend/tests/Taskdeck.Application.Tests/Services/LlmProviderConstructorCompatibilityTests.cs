using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmProviderConstructorCompatibilityTests
{
    [Theory]
    [InlineData("OpenAi", "https://openai.example/v1/chat/completions")]
    [InlineData("Ollama", "https://ollama.example/api/chat")]
    public async Task PublicConstructor_WithCallerOwnedHttpClient_ShouldPreserveConfiguredRequest(
        string providerName,
        string expectedUri)
    {
        var handler = new RecordingHandler(providerName);
        using var client = new HttpClient(handler);
        var provider = BuildProvider(providerName, client);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "caller-owned-client")],
            SystemPrompt: string.Empty));

        result.IsDegraded.Should().BeFalse();
        handler.RequestUri.Should().Be(expectedUri,
            "the public constructor accepts an ordinary caller-owned HttpClient without Taskdeck's protected handler");
        handler.RequestUri.Should().NotContain("protected-outbound.invalid");
        handler.RequestBody.Should().Contain("caller-owned-client");

        switch (providerName)
        {
            case "OpenAi":
                handler.Authorization.Should().Be("Bearer test-openai-key");
                break;
            case "Ollama":
                handler.Authorization.Should().BeNull();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null);
        }
    }

    private static ILlmProvider BuildProvider(string providerName, HttpClient client)
    {
        var settings = new LlmProviderSettings
        {
            EnableLiveProviders = true,
            OpenAi = new OpenAiProviderSettings
            {
                ApiKey = "test-openai-key",
                BaseUrl = "https://openai.example/v1",
                Model = "test-openai-model"
            },
            Ollama = new OllamaProviderSettings
            {
                BaseUrl = "https://ollama.example",
                Model = "test-ollama-model"
            }
        };

        return providerName switch
        {
            "OpenAi" => new OpenAiLlmProvider(
                client,
                settings,
                NullLogger<OpenAiLlmProvider>.Instance),
            "Ollama" => new OllamaLlmProvider(
                client,
                settings,
                NullLogger<OllamaLlmProvider>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };
    }

    private sealed class RecordingHandler(string providerName) : HttpMessageHandler
    {
        internal string? RequestUri { get; private set; }
        internal string? RequestBody { get; private set; }
        internal string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildResponseBody(providerName), Encoding.UTF8, "application/json")
            };
        }

        private static string BuildResponseBody(string name) => name switch
        {
            "OpenAi" =>
                """
                {"choices":[{"message":{"content":"OK"},"finish_reason":"stop"}],"usage":{"total_tokens":1}}
                """,
            "Ollama" =>
                """
                {"message":{"content":"OK"},"done":true,"eval_count":1,"done_reason":"stop"}
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
        };
    }
}
