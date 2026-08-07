using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Agents;
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
                "data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"},\"finish_reason\":null}],\"usage\":null}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"lo\"},\"finish_reason\":\"stop\"}],\"usage\":null}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"total_tokens\":7}}\n\n" +
                "data: [DONE]\n\n");
        });
        var provider = CreateProvider(handler, settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Select(item => item.Token).Should().Equal("Hel", "lo", string.Empty);
        events[^1].IsComplete.Should().BeTrue();
        events[^1].TokensUsed.Should().Be(7);
        events[^1].IsDegraded.Should().BeFalse();
        using var payload = JsonDocument.Parse(requestBody!);
        payload.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
        payload.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean().Should().BeTrue();
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
    public async Task StreamAsync_UsageThenErrorEvent_PreservesUsageOnTerminalError()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ => SseResponse(
            "data: {\"choices\":[],\"usage\":{\"total_tokens\":5000}}\n\n" +
            "data: {\"error\":{\"message\":\"secret upstream detail\"}}\n\n")), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].IsComplete.Should().BeTrue();
        events[0].Error.Should().Contain("upstream error");
        events[0].Error.Should().NotContain("secret upstream detail");
        events[0].TokensUsed.Should().Be(5000);
    }

    [Fact]
    public async Task StreamAsync_UsageThenReadFailure_PreservesUsageOnTerminalError()
    {
        var prefix = Encoding.UTF8.GetBytes(
            "data: {\"choices\":[],\"usage\":{\"total_tokens\":5000}}\n\n");
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
            SseStreamResponse(new PrefixThenThrowingReadStream(prefix))), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].IsComplete.Should().BeTrue();
        events[0].Error.Should().Contain("response body failed");
        events[0].TokensUsed.Should().Be(5000);
        events[0].ProviderFailureKind.Should().Be(LlmProviderFailureKind.ResponseBody);
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
        bufferedPayload.RootElement.TryGetProperty("response_format", out _).Should().BeFalse();
        bufferedPayload.RootElement.GetProperty("messages").EnumerateArray()
            .Should().OnlyContain(item => item.GetProperty("role").GetString() != "system");
    }

    [Fact]
    public async Task StreamAsync_WhenEndpointRejectsStreamingAndUsageIsAbsent_LeavesUsageUnknown()
    {
        var dispatches = 0;
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatches++;
            return dispatches == 1
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                : JsonResponse("""{"choices":[{"message":{"content":"short reply"},"finish_reason":"stop"}]}""");
        }), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].Token.Should().Be("short reply");
        events[0].TokensUsed.Should().BeNull();
        events[0].IsDegraded.Should().BeTrue();
        dispatches.Should().Be(2);
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
    public async Task CompleteAsync_UsageAbsent_PreservesUnknownUsage()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"short reply"},"finish_reason":"stop"}]}""")), BuildSettings());

        var result = await provider.CompleteAsync(Request());

        result.Content.Should().Be("short reply");
        result.TokensUsed.Should().Be(0);
        result.HasAuthoritativeTokenUsage.Should().BeFalse();
        result.ShouldSettleQuotaReservation.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteAsync_NonEmptyResponseWithZeroUsage_TreatsUsageAsUnknown()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"short reply"},"finish_reason":"stop"}],"usage":{"total_tokens":0}}""")), BuildSettings());

        var result = await provider.CompleteAsync(Request());

        result.Content.Should().Be("short reply");
        result.TokensUsed.Should().Be(0);
        result.HasAuthoritativeTokenUsage.Should().BeFalse();
        result.ShouldSettleQuotaReservation.Should().BeTrue();
    }

    [Fact]
    public async Task StreamAsync_ZeroUsageChunk_IsValidAndLeavesTerminalUsageUnknown()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ => SseResponse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: {\"choices\":[],\"usage\":{\"total_tokens\":0}}\n\n" +
            "data: [DONE]\n\n")), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Select(item => item.Token).Should().Equal("hello", string.Empty);
        events[^1].Error.Should().BeNull();
        events[^1].TokensUsed.Should().BeNull();
    }

    [Fact]
    public async Task StreamAsync_NonSseResponseWithoutUsage_LeavesUsageUnknown()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"short reply"},"finish_reason":"stop"}]}""")), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].Token.Should().Be("short reply");
        events[0].TokensUsed.Should().BeNull();
        events[0].IsDegraded.Should().BeTrue();
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

    [Fact]
    public async Task CompleteAsync_AllowsTheDevelopmentLocalhostEndpointSelectedByPolicy()
    {
        var settings = BuildSettings();
        settings.AllowLiveProvidersInDevelopment = true;
        settings.OpenAiCompatible.BaseUrl = "http://localhost:11434/v1";
        var selection = LlmProviderSelectionPolicy.Evaluate(settings, "Development");
        selection.ProviderKind.Should().Be(LlmProviderKind.OpenAiCompatible);

        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"local response"}}],"usage":{"total_tokens":3}}""")), settings,
            allowLocalhostEndpoints: true);

        var result = await provider.CompleteAsync(Request());

        result.IsDegraded.Should().BeFalse();
        result.Content.Should().Be("local response");
    }

    [Fact]
    public async Task StreamAsync_UsageAbsent_LeavesTerminalUsageUnknown()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ => SseResponse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n")), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events[^1].IsComplete.Should().BeTrue();
        events[^1].Error.Should().BeNull();
        events[^1].TokensUsed.Should().BeNull(
            "the quota layer must settle the reservation estimate when upstream usage is absent");
    }

    [Fact]
    public async Task StreamAsync_ParsesCarriageReturnOnlySseFraming()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ => SseResponse(
            "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"},\"finish_reason\":\"stop\"}]}\r\r" +
            "data: {\"choices\":[],\"usage\":{\"total_tokens\":11}}\r\r" +
            "data: [DONE]\r\r")), BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Select(item => item.Token).Should().Equal("hello", string.Empty);
        events[^1].IsComplete.Should().BeTrue();
        events[^1].TokensUsed.Should().Be(11);
        events[^1].Error.Should().BeNull();
    }

    [Theory]
    [InlineData("length", "token limit")]
    [InlineData("content_filter", "content filter")]
    [InlineData("tool_calls", "tool calls")]
    [InlineData("function_call", "function call")]
    [InlineData("vendor_reason", "non-standard")]
    public async Task StreamAsync_NonStopFinishReason_IsTerminalDegraded(string finishReason, string expectedReason)
    {
        var dispatches = 0;
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 60 };
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatches++;
            return SseResponse(
                $"data: {{\"choices\":[{{\"delta\":{{\"content\":\"partial\"}},\"finish_reason\":\"{finishReason}\"}}]}}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"total_tokens\":5}}\n\n" +
                "data: [DONE]\n\n");
        }), BuildSettings(), tracker, circuitSettings);

        var events = await CollectAsync(provider.StreamAsync(Request()));
        var second = await CollectAsync(provider.StreamAsync(Request()));

        events[^1].IsComplete.Should().BeTrue();
        events[^1].IsDegraded.Should().BeTrue();
        events[^1].DegradedReason.Should().Contain(expectedReason);
        events[^1].TokensUsed.Should().Be(5);
        second[^1].Error.Should().BeNull();
        dispatches.Should().Be(2, "normal upstream finish reasons must not open the companion circuit");
        tracker.Get("OpenAICompatible")?.State.Should().NotBe(CircuitState.Open);
    }

    [Fact]
    public async Task CompleteAsync_ContentFilterFinish_DoesNotCountAsCircuitFailure()
    {
        var dispatches = 0;
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 60 };
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatches++;
            return JsonResponse("""{"choices":[{"message":{"content":"partial"},"finish_reason":"content_filter"}],"usage":{"total_tokens":4}}""");
        }), BuildSettings(), tracker, circuitSettings);

        var first = await provider.CompleteAsync(Request());
        var second = await provider.CompleteAsync(Request());

        first.IsDegraded.Should().BeTrue();
        second.IsDegraded.Should().BeTrue();
        dispatches.Should().Be(2);
        tracker.Get("OpenAICompatible")?.State.Should().NotBe(CircuitState.Open);
    }

    [Fact]
    public async Task CompleteAsync_RejectedAboveDispatchTracker_DoesNotChargeTheCircuit()
    {
        var innerDispatches = 0;
        var outerAttempts = 0;
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 60 };

        // LlmProviderRegistration wires the telemetry and egress handlers OUTSIDE
        // LlmDispatchTrackingHandler, so a rejection above the tracker never reaches the
        // upstream and never marks the request dispatched. CreateProvider puts the tracker
        // outermost, which cannot reproduce that ordering, so build the pipeline by hand.
        var inner = new StubHttpMessageHandler(_ =>
        {
            innerDispatches++;
            return JsonResponse("""{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"total_tokens":3}}""");
        });
        var provider = new OpenAiCompatibleLlmProvider(
            new HttpClient(new RejectFirstSendHandler(() => outerAttempts++)
            {
                InnerHandler = new LlmDispatchTrackingHandler { InnerHandler = inner }
            }),
            BuildSettings(),
            NullLogger<OpenAiCompatibleLlmProvider>.Instance,
            tracker,
            circuitSettings,
            new LlmProviderRuntimePolicy(
                AllowGeneralProviderLocalhost: false,
                AllowOllamaLocalhost: false));

        var rejected = await provider.CompleteAsync(Request());
        var admitted = await provider.CompleteAsync(Request());

        outerAttempts.Should().Be(2, "both requests must be offered to the outer handler");
        rejected.ShouldSettleQuotaReservation.Should().BeFalse(
            "a request that never left the process must not consume its quota reservation");
        rejected.IsDegraded.Should().BeTrue();
        innerDispatches.Should().Be(
            1,
            "the pre-admission rejection must not open the circuit, so the retry still reaches the upstream");
        admitted.IsDegraded.Should().BeFalse();
        tracker.Get("OpenAICompatible")?.State.Should().NotBe(CircuitState.Open);
    }

    /// <summary>
    /// Stands in for an outer telemetry/egress handler that refuses the first send before the
    /// dispatch tracker sees it, then admits every later send.
    /// </summary>
    private sealed class RejectFirstSendHandler : DelegatingHandler
    {
        private readonly Action _onAttempt;
        private bool _rejected;

        public RejectFirstSendHandler(Action onAttempt) => _onAttempt = onAttempt;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _onAttempt();
            if (!_rejected)
            {
                _rejected = true;
                throw new HttpRequestException("outbound telemetry protection refused the request");
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    [Theory]
    [InlineData("""{"choices":[{"message":{"content":null},"finish_reason":"content_filter"}],"usage":{"total_tokens":4}}""", "content filter")]
    [InlineData("""{"choices":[{"message":{"content":null,"refusal":"sensitive vendor refusal detail"},"finish_reason":"stop"}],"usage":{"total_tokens":4}}""", "refused")]
    public async Task CompleteAsync_NullContentFilterOrRefusal_IsSanitizedSuccess(
        string responseBody,
        string expectedReason)
    {
        var dispatches = 0;
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 60 };
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatches++;
            return JsonResponse(responseBody);
        }), BuildSettings(), tracker, circuitSettings);

        var first = await provider.CompleteAsync(Request());
        var second = await provider.CompleteAsync(Request());

        first.Content.Should().BeEmpty();
        first.IsDegraded.Should().BeTrue();
        first.DegradedReason.Should().Contain(expectedReason);
        first.DegradedReason.Should().NotContain("sensitive vendor refusal detail");
        first.CountsAsProviderFailure.Should().BeFalse();
        second.IsDegraded.Should().BeTrue();
        dispatches.Should().Be(2, "a sanitized refusal/filter outcome must not open the companion circuit");
        tracker.Get("OpenAICompatible")?.State.Should().NotBe(CircuitState.Open);
    }

    [Fact]
    public async Task CompleteAsync_NullContentWithOrdinaryStop_RemainsProtocolFailure()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":null},"finish_reason":"stop"}],"usage":{"total_tokens":4}}""")), BuildSettings());

        var result = await provider.CompleteAsync(Request());

        result.IsDegraded.Should().BeTrue();
        result.ProviderFailureKind.Should().Be(LlmProviderFailureKind.Protocol);
    }

    [Theory]
    [InlineData("{\"choices\":[1]}")]
    [InlineData("{\"choices\":[{\"delta\":\"bad\",\"finish_reason\":null}]}")]
    [InlineData("{\"choices\":[{\"delta\":{\"content\":1},\"finish_reason\":null}]}")]
    [InlineData("{\"choices\":[{\"delta\":{},\"finish_reason\":1}]}")]
    [InlineData("{\"choices\":[],\"usage\":{\"total_tokens\":\"many\"}}")]
    public async Task StreamAsync_HostileSseSchema_EmitsExplicitTerminalError(string data)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => SseResponse($"data: {data}\n\n")),
            BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].IsComplete.Should().BeTrue();
        events[0].Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StreamAsync_HttpTransportFailure_EmitsExplicitTerminalError()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("boom"))),
            BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].IsComplete.Should().BeTrue();
        events[0].Error.Should().Contain("transport failed");
        events[0].ProviderFailureKind.Should().Be(LlmProviderFailureKind.Transport);
    }

    [Fact]
    public async Task StreamAsync_ResponseBodyIoFailure_EmitsExplicitTerminalError()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => SseStreamResponse(new ThrowingReadStream())),
            BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].Error.Should().Contain("response body failed");
        events[0].ProviderFailureKind.Should().Be(LlmProviderFailureKind.ResponseBody);
    }

    [Fact]
    public async Task StreamAsync_StalledBodyAfterHeaders_EmitsTimeoutTerminalEvent()
    {
        var settings = BuildSettings();
        settings.OpenAiCompatible.TimeoutSeconds = 1;
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => SseStreamResponse(new BlockingReadStream())),
            settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].IsComplete.Should().BeTrue();
        events[0].Error.Should().Contain("timed out");
        events[0].ProviderFailureKind.Should().Be(LlmProviderFailureKind.Timeout);
    }

    [Fact]
    public async Task StreamAsync_OversizedLine_EmitsBoundedTerminalError()
    {
        var settings = BuildSettings();
        settings.OpenAiCompatible.MaxSseLineBytes = 256;
        settings.OpenAiCompatible.MaxSseEventBytes = 512;
        settings.OpenAiCompatible.MaxResponseBytes = 1024;
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => SseResponse("data: " + new string('x', 300) + "\n\n")),
            settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].Error.Should().Contain("safety limit");
    }

    [Fact]
    public async Task StreamAsync_OversizedEvent_EmitsBoundedTerminalError()
    {
        var settings = BuildSettings();
        settings.OpenAiCompatible.MaxSseLineBytes = 1024;
        settings.OpenAiCompatible.MaxSseEventBytes = 512;
        settings.OpenAiCompatible.MaxResponseBytes = 4096;
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => SseResponse("data: " + new string('x', 600) + "\n\n")),
            settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].Error.Should().Contain("safety limit");
    }

    [Fact]
    public async Task StreamAsync_AggregateResponseBudget_EmitsBoundedTerminalError()
    {
        var settings = BuildSettings();
        settings.OpenAiCompatible.MaxSseLineBytes = 256;
        settings.OpenAiCompatible.MaxSseEventBytes = 512;
        settings.OpenAiCompatible.MaxResponseBytes = 1024;
        var oneEvent = "data: {\"choices\":[{\"delta\":{\"content\":\"" + new string('x', 80) + "\"},\"finish_reason\":null}]}\n\n";
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => SseResponse(string.Concat(Enumerable.Repeat(oneEvent, 12)))),
            settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events[^1].IsComplete.Should().BeTrue();
        events[^1].Error.Should().Contain("safety limit");
    }

    [Theory]
    [InlineData("utf16")]
    [InlineData("utf32")]
    public async Task StreamAsync_NonUtf8BomPayload_IsRejected(string encodingName)
    {
        const string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";
        var encoding = encodingName == "utf16"
            ? Encoding.Unicode
            : new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(sse)).ToArray();
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => SseBytesResponse(bytes)),
            BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].Error.Should().Contain("response body failed");
    }

    [Fact]
    public async Task StreamAsync_EmojiAtExactRawUtf8LineLimit_IsAccepted()
    {
        var content = new string('x', 220) + "😀";
        var line = $"data: {{\"choices\":[{{\"delta\":{{\"content\":\"{content}\"}},\"finish_reason\":\"stop\"}}]}}";
        var lineBytes = Encoding.UTF8.GetByteCount(line);
        lineBytes.Should().BeGreaterThanOrEqualTo(256);
        var settings = BuildSettings();
        settings.OpenAiCompatible.MaxSseLineBytes = lineBytes;
        settings.OpenAiCompatible.MaxSseEventBytes = Math.Max(512, lineBytes);
        var provider = CreateProvider(new StubHttpMessageHandler(_ => SseResponse(
            line + "\n\n" +
            "data: [DONE]\n\n")), settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events[0].Token.Should().Be(content);
        events[^1].Error.Should().BeNull();
    }

    [Fact]
    public async Task StreamAsync_InitialUtf8Bom_IsAcceptedAndCounted()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sse)).ToArray();
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => SseBytesResponse(bytes)),
            BuildSettings());

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Select(item => item.Token).Should().Equal("hello", string.Empty);
        events[^1].Error.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_EgressViolation_IsRethrownUnchanged()
    {
        var expected = CreateEgressViolationException();
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(expected)),
            BuildSettings());

        var act = () => provider.CompleteAsync(Request());

        var thrown = await act.Should().ThrowAsync<EgressViolationException>();
        thrown.Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task StreamAsync_EgressViolation_IsRethrownUnchanged()
    {
        var expected = CreateEgressViolationException();
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(expected)),
            BuildSettings());

        var act = () => CollectAsync(provider.StreamAsync(Request()));

        var thrown = await act.Should().ThrowAsync<EgressViolationException>();
        thrown.Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task StreamAsync_OversizedNonSseBody_EmitsBoundedTerminalError()
    {
        var settings = BuildSettings();
        settings.OpenAiCompatible.MaxResponseBytes = 1024;
        settings.OpenAiCompatible.MaxSseEventBytes = 512;
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => JsonResponse(new string('x', 1025))),
            settings);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        events.Should().ContainSingle();
        events[0].Error.Should().Contain("safety limit");
    }

    [Fact]
    public async Task CompleteAsync_OversizedJsonBody_ReturnsDegradedFallback()
    {
        var settings = BuildSettings();
        settings.OpenAiCompatible.MaxResponseBytes = 1024;
        settings.OpenAiCompatible.MaxSseEventBytes = 512;
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ => JsonResponse(new string('x', 1025))),
            settings);

        var result = await provider.CompleteAsync(Request());

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("safety limit");
    }

    [Fact]
    public async Task StreamAsync_BodyFailuresOpenCompanionCircuit()
    {
        var settings = BuildSettings();
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 60 };
        var dispatches = 0;
        var provider = CreateProvider(
            new StubHttpMessageHandler(_ =>
            {
                dispatches++;
                return SseResponse("data: not-json\n\n");
            }),
            settings,
            tracker,
            circuitSettings);

        var first = await CollectAsync(provider.StreamAsync(Request()));
        var second = await CollectAsync(provider.StreamAsync(Request()));

        first[^1].Error.Should().Contain("malformed JSON");
        second.Should().ContainSingle();
        second[0].Error.Should().Contain("circuit is open");
        dispatches.Should().Be(1);
        tracker.Get("OpenAICompatible")!.State.Should().Be(CircuitState.Open);
    }

    [Theory]
    [InlineData("malformed", (int)LlmProviderFailureKind.Protocol)]
    [InlineData("body-io", (int)LlmProviderFailureKind.ResponseBody)]
    [InlineData("oversized", (int)LlmProviderFailureKind.ResponseLimit)]
    [InlineData("stalled", (int)LlmProviderFailureKind.Timeout)]
    public async Task CompleteAsync_PostHeaderFailuresOpenCompanionCircuit(
        string failureMode,
        int expectedFailureKind)
    {
        var settings = BuildSettings();
        settings.OpenAiCompatible.TimeoutSeconds = 1;
        settings.OpenAiCompatible.MaxResponseBytes = 1024;
        settings.OpenAiCompatible.MaxSseEventBytes = 512;
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 60 };
        var dispatches = 0;
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatches++;
            return failureMode switch
            {
                "malformed" => JsonResponse("{}"),
                "body-io" => JsonStreamResponse(new ThrowingReadStream()),
                "oversized" => JsonResponse(new string('x', 1025)),
                "stalled" => JsonStreamResponse(new BlockingReadStream()),
                _ => throw new InvalidOperationException("Unknown test mode")
            };
        }), settings, tracker, circuitSettings);

        var first = await provider.CompleteAsync(Request());
        var second = await provider.CompleteAsync(Request());

        first.IsDegraded.Should().BeTrue();
        first.ProviderFailureKind.Should().Be((LlmProviderFailureKind)expectedFailureKind);
        first.CountsAsProviderFailure.Should().BeTrue();
        second.DegradedReason.Should().Contain("circuit is open");
        dispatches.Should().Be(1);
        tracker.Get("OpenAICompatible")!.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task CompleteAsync_SuccessResetsConsecutiveBodyFailures()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse("{}"),
            JsonResponse("""{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"total_tokens":2}}"""),
            JsonResponse("{}"),
            JsonResponse("""{"choices":[{"message":{"content":"ok again"},"finish_reason":"stop"}],"usage":{"total_tokens":3}}""")
        ]);
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 2, BreakDurationSeconds = 60 };
        var dispatches = 0;
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatches++;
            return responses.Dequeue();
        }), BuildSettings(), tracker, circuitSettings);

        var results = new List<LlmCompletionResult>();
        for (var i = 0; i < 4; i++)
            results.Add(await provider.CompleteAsync(Request()));

        results[0].IsDegraded.Should().BeTrue();
        results[1].IsDegraded.Should().BeFalse();
        results[2].IsDegraded.Should().BeTrue();
        results[3].IsDegraded.Should().BeFalse();
        dispatches.Should().Be(4, "each success resets the prior consecutive body failure");
        tracker.Get("OpenAICompatible")!.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task StreamAsync_DisposingHalfOpenProbe_ReopensForConfiguredCooldown()
    {
        var dispatches = 0;
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 1 };
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatches++;
            return dispatches switch
            {
                1 => SseResponse("data: not-json\n\n"),
                2 => SseResponse("data: {\"choices\":[{\"delta\":{\"content\":\"probe\"},\"finish_reason\":null}]}\n\n"),
                _ => SuccessfulSseResponse("recovered")
            };
        }), BuildSettings(), tracker, circuitSettings);

        await CollectAsync(provider.StreamAsync(Request()));
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        var enumerator = provider.StreamAsync(Request()).GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Token.Should().Be("probe");
        await enumerator.DisposeAsync();

        var rejectedDuringCooldown = await CollectAsync(provider.StreamAsync(Request()));
        rejectedDuringCooldown[^1].Error.Should().Contain("circuit is open");
        dispatches.Should().Be(2);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var recovered = await CollectAsync(provider.StreamAsync(Request()));

        recovered[^1].Error.Should().BeNull();
        dispatches.Should().Be(3);
        tracker.Get("OpenAICompatible")!.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task StreamAsync_CancellingHalfOpenProbe_ReopensForConfiguredCooldown()
    {
        var dispatches = 0;
        var tracker = new CircuitBreakerStateTracker();
        var circuitSettings = new CircuitBreakerSettings { FailureThreshold = 1, BreakDurationSeconds = 1 };
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatches++;
            return dispatches switch
            {
                1 => SseResponse("data: not-json\n\n"),
                2 => SseStreamResponse(new BlockingReadStream()),
                _ => SuccessfulSseResponse("recovered")
            };
        }), BuildSettings(), tracker, circuitSettings);

        await CollectAsync(provider.StreamAsync(Request()));
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Func<Task<List<LlmTokenEvent>>> cancelled = () =>
            CollectAsync(provider.StreamAsync(Request(), cancellation.Token));
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        var rejectedDuringCooldown = await CollectAsync(provider.StreamAsync(Request()));
        rejectedDuringCooldown[^1].Error.Should().Contain("circuit is open");
        dispatches.Should().Be(2);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var recovered = await CollectAsync(provider.StreamAsync(Request()));

        recovered[^1].Error.Should().BeNull();
        dispatches.Should().Be(3);
        tracker.Get("OpenAICompatible")!.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task ProbeAsync_ProviderTimeout_ReturnsUnavailableInsteadOfThrowing()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(new TaskCanceledException("upstream timeout"))),
            BuildSettings());

        var health = await provider.ProbeAsync();

        health.IsAvailable.Should().BeFalse();
        health.IsProbed.Should().BeTrue();
        health.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task CompleteAsync_WhenProviderIsNotAuthoritativelySelected_DoesNotDispatch()
    {
        var settings = BuildSettings();
        settings.Provider = "Mock";
        var dispatched = false;
        var provider = CreateProvider(new StubHttpMessageHandler(_ =>
        {
            dispatched = true;
            return JsonResponse("{}");
        }), settings);

        var result = await provider.CompleteAsync(Request());

        result.IsDegraded.Should().BeTrue();
        dispatched.Should().BeFalse();
    }

    [Fact]
    public async Task StreamAsync_EmitsEachServerDerivedAttributionHeaderExactlyOnce()
    {
        HttpRequestMessage? observed = null;
        var provider = CreateProvider(new StubHttpMessageHandler(request =>
        {
            observed = request;
            return SseResponse(
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
                "data: [DONE]\n\n");
        }), BuildSettings());
        var attribution = new LlmRequestAttribution(
            Guid.NewGuid(), "corr-1", LlmRequestSourceSurface.Chat, Guid.NewGuid(), Guid.NewGuid());

        await CollectAsync(provider.StreamAsync(Request(attribution)));

        observed.Should().NotBeNull();
        foreach (var header in new[]
                 {
                     LlmRequestAttributionMapper.CorrelationHeader,
                     LlmRequestAttributionMapper.SourceSurfaceHeader,
                     LlmRequestAttributionMapper.UserTokenHeader,
                     LlmRequestAttributionMapper.BoardTokenHeader,
                     LlmRequestAttributionMapper.SessionTokenHeader
                 })
        {
            observed!.Headers.GetValues(header).Should().ContainSingle();
        }
        observed!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        observed.Headers.GetValues("X-Title").Should().ContainSingle();
    }

    [Fact]
    public void LlmTokenEvent_PreservesSixParameterConstructorAndCompatibleWireShape()
    {
        typeof(LlmTokenEvent).GetConstructors()
            .Should().ContainSingle(constructor => constructor.GetParameters().Length == 6);
        var normalJson = JsonSerializer.Serialize(new LlmTokenEvent("token", false, Provider: "OpenAI", Model: "model"));
        var degradedJson = JsonSerializer.Serialize(new LlmTokenEvent("", true, TokensUsed: 4, Provider: "OpenAICompatible", Model: "vendor/model")
        {
            IsDegraded = true,
            DegradedReason = "reason"
        });

        normalJson.Should().Be(
            "{\"Token\":\"token\",\"IsComplete\":false,\"Error\":null,\"TokensUsed\":null,\"Provider\":\"OpenAI\",\"Model\":\"model\"}");
        degradedJson.Should().Be(
            "{\"Token\":\"\",\"IsComplete\":true,\"Error\":null,\"TokensUsed\":4,\"Provider\":\"OpenAICompatible\",\"Model\":\"vendor/model\",\"IsDegraded\":true,\"DegradedReason\":\"reason\"}");
    }

    private static OpenAiCompatibleLlmProvider CreateProvider(
        HttpMessageHandler handler,
        LlmProviderSettings settings,
        CircuitBreakerStateTracker? tracker = null,
        CircuitBreakerSettings? circuitSettings = null,
        bool allowLocalhostEndpoints = false)
    {
        HttpMessageHandler pipeline = handler;
        if (tracker is not null)
        {
            pipeline = new LlmDispatchTrackingHandler
            {
                InnerHandler = handler
            };
        }

        return new OpenAiCompatibleLlmProvider(
            new HttpClient(pipeline),
            settings,
            NullLogger<OpenAiCompatibleLlmProvider>.Instance,
            tracker,
            circuitSettings,
            new LlmProviderRuntimePolicy(
                AllowGeneralProviderLocalhost: allowLocalhostEndpoints,
                AllowOllamaLocalhost: allowLocalhostEndpoints));
    }

    private static ChatCompletionRequest Request(LlmRequestAttribution? attribution = null) =>
        new([new ChatCompletionMessage("user", "hello")], Attribution: attribution);

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

    private static HttpResponseMessage SseStreamResponse(Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return response;
    }

    private static HttpResponseMessage SseBytesResponse(byte[] bytes) =>
        SseStreamResponse(new MemoryStream(bytes, writable: false));

    private static EgressViolationException CreateEgressViolationException() => new(
        new EgressViolation(
            "blocked.example",
            "https://blocked.example/",
            EgressViolationType.UnknownHost,
            "blocked"));

    private static HttpResponseMessage JsonStreamResponse(Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return response;
    }

    private static HttpResponseMessage SuccessfulSseResponse(string content) => SseResponse(
        $"data: {{\"choices\":[{{\"delta\":{{\"content\":\"{content}\"}},\"finish_reason\":\"stop\"}}]}}\n\n" +
        "data: {\"choices\":[],\"usage\":{\"total_tokens\":3}}\n\n" +
        "data: [DONE]\n\n");

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

    private abstract class TestReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingReadStream : TestReadStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("broken SSE body"));
    }

    private sealed class PrefixThenThrowingReadStream(byte[] prefix) : TestReadStream
    {
        private int _offset;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_offset >= prefix.Length)
                return ValueTask.FromException<int>(new IOException("broken SSE body after usage"));

            var length = Math.Min(buffer.Length, prefix.Length - _offset);
            prefix.AsMemory(_offset, length).CopyTo(buffer);
            _offset += length;
            return ValueTask.FromResult(length);
        }
    }

    private sealed class BlockingReadStream : TestReadStream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
