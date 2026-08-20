using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Resilience tests for LLM providers: garbage responses, rate limiting (429),
/// network timeouts, and empty/null responses. Validates that every failure mode
/// produces a degraded response rather than an unhandled exception.
/// Covers issue #720 (TST-67).
/// </summary>
public class LlmProviderResilienceTests
{
    [Fact]
    public async Task OpenAiCompatible_CompleteAsync_GarbageResponseBody_ReturnsDegradedResult()
    {
        var settings = new LlmProviderSettings
        {
            EnableLiveProviders = true,
            Provider = "OpenAICompatible",
            OpenAiCompatible = new OpenAiCompatibleProviderSettings
            {
                ApiKey = "test-compatible-key",
                BaseUrl = "https://api.example.test/v1",
                Model = "vendor/model"
            }
        };
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not a compatible completion", Encoding.UTF8, "text/plain")
        });
        var provider = new OpenAiCompatibleLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiCompatibleLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "create a card")]));

        result.IsDegraded.Should().BeTrue();
        result.Provider.Should().Be("OpenAICompatible");
    }

    // ── OpenAI: Garbage Response (Invalid JSON Body) ─────────────────

    [Fact]
    public async Task OpenAi_CompleteAsync_GarbageResponseBody_ReturnsDegradedResult()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "this is not json at all <html>502 Bad Gateway</html>",
                    Encoding.UTF8,
                    "text/html")
            });
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "create a card")]));

        result.Should().NotBeNull("provider must never return null");
        result.IsDegraded.Should().BeTrue("garbage response should be flagged as degraded");
        result.DegradedReason.Should().NotBeNullOrWhiteSpace(
            "degraded reason should explain why the response is degraded");
        result.Provider.Should().Be("OpenAI");
    }

    [Fact]
    public async Task OpenAi_CompleteAsync_EmptyResponseBody_ReturnsDegradedResult()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json")
            });
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "list my tasks")]));

        result.Should().NotBeNull();
        result.IsDegraded.Should().BeTrue("empty body should produce degraded response");
    }

    [Fact]
    public async Task OpenAi_CompleteAsync_ValidJsonButMissingChoices_ReturnsDegradedResult()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"error": "unexpected format", "usage": {"total_tokens": 0}}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "hello")]));

        result.Should().NotBeNull();
        result.IsDegraded.Should().BeTrue("response with no choices array should be degraded");
    }

    // ── OpenAI: Rate Limiting (429) ─────────────────────────────────

    [Fact]
    public async Task OpenAi_CompleteAsync_Returns429RateLimited_ReturnsDegradedResult()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent(
                    """{"error": {"message": "Rate limit exceeded", "type": "tokens", "code": "rate_limit_exceeded"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.Add("Retry-After", "30");
            return response;
        });
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "create a card")]));

        result.Should().NotBeNull("429 should produce a degraded result, not throw");
        result.IsDegraded.Should().BeTrue("rate-limited response should be flagged as degraded");
        result.DegradedReason.Should().Contain("failed",
            "degraded reason should indicate the request failed");
        result.Provider.Should().Be("OpenAI");
    }

    [Fact]
    public async Task OpenAi_CompleteAsync_Returns429_DoesNotThrowException()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent(
                    """{"error": {"message": "Too many requests"}}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var act = async () => await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "test")]));

        await act.Should().NotThrowAsync(
            "rate limiting must never cause an unhandled exception");
    }

    // ── OpenAI: Network Timeout ─────────────────────────────────────

    [Fact]
    public async Task OpenAi_CompleteAsync_HttpClientThrowsTimeout_PropagatesException()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        var logger = new InMemoryLogger<OpenAiLlmProvider>();
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, logger);

        // TaskCanceledException from HttpClient timeout is an OperationCanceledException.
        // The provider intentionally re-throws this exception so that the caller (e.g., the
        // controller) can handle the timeout appropriately (e.g., by returning 504 Gateway Timeout).
        var act = async () => await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "create a card")]));

        await act.Should().ThrowAsync<OperationCanceledException>(
            "timeout exceptions should propagate so the controller layer can handle them");
    }

    // ── OpenAI: Tool Calling with Garbage Response ──────────────────

    [Fact]
    public async Task OpenAi_CompleteWithToolsAsync_GarbageResponse_ReturnsDegradedToolResult()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "NOT JSON AT ALL",
                    Encoding.UTF8,
                    "text/plain")
            });
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var tools = Array.Empty<TaskdeckToolSchema>();
        var result = await provider.CompleteWithToolsAsync(
            new ChatCompletionRequest([new ChatCompletionMessage("User", "list cards")]),
            tools);

        result.Should().NotBeNull();
        result.IsDegraded.Should().BeTrue("garbage tool-calling response should be degraded");
        result.IsComplete.Should().BeTrue("degraded tool result should signal completion");
    }

    [Fact]
    public async Task OpenAi_CompleteWithToolsAsync_Returns500_ReturnsDegradedToolResult()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    """{"error": {"message": "Internal server error"}}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var tools = Array.Empty<TaskdeckToolSchema>();
        var result = await provider.CompleteWithToolsAsync(
            new ChatCompletionRequest([new ChatCompletionMessage("User", "list cards")]),
            tools);

        result.Should().NotBeNull();
        result.IsDegraded.Should().BeTrue("500 response should produce degraded tool result");
        result.IsComplete.Should().BeTrue();
    }

    // ── OpenAI: Health / Probe with degraded provider ───────────────

    [Fact]
    public async Task OpenAi_ProbeAsync_WhenProviderReturnsGarbage_ReportsUnhealthy()
    {
        var settings = BuildOpenAiSettings();
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json", Encoding.UTF8, "text/plain")
            });
        var provider = new OpenAiLlmProvider(
            new HttpClient(handler), settings, NullLogger<OpenAiLlmProvider>.Instance);

        var health = await provider.ProbeAsync();

        health.IsAvailable.Should().BeFalse("probe should detect degraded responses as unhealthy");
        health.IsProbed.Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static LlmProviderSettings BuildOpenAiSettings()
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
