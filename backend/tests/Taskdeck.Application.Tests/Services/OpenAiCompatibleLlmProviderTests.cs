using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OpenAiCompatibleLlmProviderTests
{
    [Fact]
    public async Task StreamAsync_ParsesRealSseDeltas_AndSendsStreamTrue()
    {
        var settings = BuildSettings();
        string? requestBody = null;
        string? extraHeader = null;
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(ct);
            extraHeader = request.Headers.GetValues("X-Title").Single();
            return SseResponse(
                "data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"lo\"},\"finish_reason\":\"stop\"}],\"usage\":{\"total_tokens\":7}}\n\n");
        });
        var provider = CreateProvider(handler, settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Select(item => item.Token).Should().Equal("Hel", "lo", string.Empty);
        events[^1].IsComplete.Should().BeTrue();
        events[^1].TokensUsed.Should().Be(7);
        events[^1].IsDegraded.Should().BeFalse();
        using var payload = JsonDocument.Parse(requestBody!);
        payload.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
        payload.RootElement.TryGetProperty("response_format", out _).Should().BeFalse(
            "streaming chat sends readable deltas rather than the buffered extraction envelope");
        extraHeader.Should().Be("Taskdeck tests");
    }

    [Fact]
    public async Task StreamAsync_MalformedSse_EmitsExplicitCompletionError()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ => SseResponse("data: not-json\n\n")), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].IsComplete.Should().BeTrue();
        events[0].Error.Should().Contain("malformed JSON");
    }

    [Fact]
    public async Task StreamAsync_MidStreamError_PreservesDeliveredDelta_AndSignalsCompletionError()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ => SseResponse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"error\":{\"message\":\"upstream failed\"}}\n\n")), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().HaveCount(2);
        events[0].Token.Should().Be("partial");
        events[0].IsComplete.Should().BeFalse();
        events[1].IsComplete.Should().BeTrue();
        events[1].Error.Should().Contain("upstream error");
    }

    [Fact]
    public async Task StreamAsync_WhenEndpointRejectsStreaming_EmitsBufferedFallbackMetadata()
    {
        var settings = BuildSettings();
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync(ct));
            return requests.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                : JsonResponse("""{"choices":[{"message":{"content":"fallback reply"}}],"usage":{"total_tokens":9}}""");
        });
        var provider = CreateProvider(handler, settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].Token.Should().Be("fallback reply");
        events[0].IsComplete.Should().BeTrue();
        events[0].IsDegraded.Should().BeTrue();
        events[0].DegradedReason.Should().Contain("rejected SSE streaming");
        requests.Should().HaveCount(2);
        using var streamedPayload = JsonDocument.Parse(requests[0]);
        using var bufferedPayload = JsonDocument.Parse(requests[1]);
        streamedPayload.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
        bufferedPayload.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_WhenResponseFormatIsRejected_RetriesWithPromptOnlyJson()
    {
        var requestBodies = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            requestBodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return requestBodies.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                : JsonResponse("""{"choices":[{"message":{"content":"plain fallback"}}],"usage":{"total_tokens":4}}""");
        });
        var provider = CreateProvider(handler, BuildSettings());

        var result = await provider.CompleteAsync(Request());

        result.Content.Should().Be("plain fallback");
        result.IsDegraded.Should().BeFalse();
        requestBodies.Should().HaveCount(2);
        using var initialPayload = JsonDocument.Parse(requestBodies[0]);
        using var fallbackPayload = JsonDocument.Parse(requestBodies[1]);
        initialPayload.RootElement.TryGetProperty("response_format", out _).Should().BeTrue();
        fallbackPayload.RootElement.TryGetProperty("response_format", out _).Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_PropagatesCancellation()
    {
        var handler = new StubHttpMessageHandler((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));
        var provider = CreateProvider(handler, BuildSettings());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await CollectAsync(provider.StreamAsync(Request(), cancellation.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static OpenAiCompatibleLlmProvider CreateProvider(HttpMessageHandler handler, LlmProviderSettings settings) =>
        new(new HttpClient(handler), settings, NullLogger<OpenAiCompatibleLlmProvider>.Instance);

    private static ChatCompletionRequest Request() => new([new ChatCompletionMessage("user", "hello")]);

    private static async Task<List<LlmTokenEvent>> CollectAsync(IAsyncEnumerable<LlmTokenEvent> stream)
    {
        var events = new List<LlmTokenEvent>();
        await foreach (var item in stream)
            events.Add(item);
        return events;
    }

    private static HttpResponseMessage SseResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
    };

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static LlmProviderSettings BuildSettings() => new()
    {
        EnableLiveProviders = true,
        Provider = "OpenAICompatible",
        OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://api.example.test/v1",
            Model = "vendor/model",
            TimeoutSeconds = 30,
            ExtraHeaders = new Dictionary<string, string> { ["X-Title"] = "Taskdeck tests" }
        }
    };
}
