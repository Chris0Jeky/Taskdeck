using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmProviderResponseDeadlineTests
{
    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    [InlineData("Ollama")]
    public async Task CompleteAsync_ShouldDegradeOnConfiguredDeadline_WhenBodySlowDripsUnderByteLimit(
        string providerName)
    {
        var stream = new SlowDripStream(TimeSpan.FromMilliseconds(50));
        var provider = BuildProvider(providerName, stream, timeoutSeconds: 1);
        using var testSafety = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await provider.CompleteAsync(
            BuildRequest(),
            testSafety.Token);

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("timed out");
        stream.BytesRead.Should().BeGreaterThan(0);
        stream.BytesRead.Should().BeLessThan(LlmProviderResponseReader.MaxResponseBytes);
        testSafety.IsCancellationRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    public async Task CompleteWithToolsAsync_ShouldDegradeOnConfiguredDeadline_WhenBodySlowDrips(
        string providerName)
    {
        var stream = new SlowDripStream(TimeSpan.FromMilliseconds(50));
        var provider = BuildProvider(providerName, stream, timeoutSeconds: 1);
        using var testSafety = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await provider.CompleteWithToolsAsync(
            BuildRequest(),
            [],
            ct: testSafety.Token);

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("timed out");
        result.TokensUsed.Should().Be(0);
        stream.BytesRead.Should().BeGreaterThan(0);
        testSafety.IsCancellationRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    [InlineData("Ollama")]
    public async Task CompleteAsync_ShouldThrow_WhenCallerCancelsDuringBodyRead(string providerName)
    {
        var stream = new SlowDripStream(TimeSpan.FromMilliseconds(25));
        var provider = BuildProvider(providerName, stream, timeoutSeconds: 30);
        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await provider.CompleteAsync(
            BuildRequest(),
            callerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        stream.BytesRead.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    public async Task CompleteWithToolsAsync_ShouldThrow_WhenCallerCancelsDuringBodyRead(
        string providerName)
    {
        var stream = new SlowDripStream(TimeSpan.FromMilliseconds(25));
        var provider = BuildProvider(providerName, stream, timeoutSeconds: 30);
        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await provider.CompleteWithToolsAsync(
            BuildRequest(),
            [],
            ct: callerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        stream.BytesRead.Should().BeGreaterThan(0);
    }

    private static ChatCompletionRequest BuildRequest()
        => new([new ChatCompletionMessage("User", "create card 'deadline regression'")]);

    private static ILlmProvider BuildProvider(
        string providerName,
        SlowDripStream responseStream,
        int timeoutSeconds)
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(responseStream)
            });
        var httpClient = new HttpClient(handler);
        var settings = new LlmProviderSettings
        {
            EnableLiveProviders = true,
            AllowLiveProvidersInDevelopment = true,
            Provider = providerName,
            OpenAi = new OpenAiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                TimeoutSeconds = timeoutSeconds
            },
            Gemini = new GeminiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                Model = "gemini-2.5-flash",
                TimeoutSeconds = timeoutSeconds
            },
            Ollama = new OllamaProviderSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "llama3.2",
                TimeoutSeconds = timeoutSeconds,
                AllowLocalhostEndpoints = true
            }
        };

        return providerName switch
        {
            "OpenAI" => new OpenAiLlmProvider(
                httpClient,
                settings,
                NullLogger<OpenAiLlmProvider>.Instance),
            "Gemini" => new GeminiLlmProvider(
                httpClient,
                settings,
                NullLogger<GeminiLlmProvider>.Instance),
            "Ollama" => new OllamaLlmProvider(
                httpClient,
                settings,
                NullLogger<OllamaLlmProvider>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };
    }
}
