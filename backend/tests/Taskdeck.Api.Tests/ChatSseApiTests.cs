using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ChatSseApiTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(5);

    private readonly TestWebApplicationFactory _baseFactory;

    public ChatSseApiTests(TestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task GetStream_ShouldFlushDeltaBeforeGatedProviderCompletes()
    {
        var provider = new GatedOpenAiCompatibleProviderStub();
        using var factory = CreateFactory(provider);
        using var client = factory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;

        await ApiTestHarness.AuthenticateAsync(client, "chat-sse-gated");
        var session = await CreateSessionAsync(client, "Gated SSE stream");

        using var requestDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync(
            $"/api/llm/chat/sessions/{session.Id}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            requestDeadline.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertSingleEventStreamContentType(response);

        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(requestDeadline.Token);
            using var reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

            var delta = await ReadEventAsync(reader);
            delta.EventType.Should().Be("message.delta");
            delta.Payload.GetProperty("token").GetString().Should().Be("first");
            delta.Payload.GetProperty("isComplete").GetBoolean().Should().BeFalse();
            provider.TerminalEventProduced.Task.IsCompleted.Should().BeFalse(
                "the provider is held after the first token, so completion must not be observable yet");

            provider.Release();

            var complete = await ReadEventAsync(reader);
            complete.EventType.Should().Be("message.complete");
            complete.Payload.GetProperty("token").GetString().Should().Be(" complete");
            complete.Payload.GetProperty("isComplete").GetBoolean().Should().BeTrue();
            await provider.TerminalEventProduced.Task.WaitAsync(ReadDeadline);
        }
        finally
        {
            provider.Release();
        }
    }

    [Fact]
    public async Task GetStream_ShouldKeepBufferedProviderCompatibleWithDeltaThenCompleteFrames()
    {
        using var factory = CreateFactory(new BufferedOpenAiCompatibleProviderStub());
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "chat-sse-buffered");
        var session = await CreateSessionAsync(client, "Buffered SSE stream");

        using var requestDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync(
            $"/api/llm/chat/sessions/{session.Id}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            requestDeadline.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertSingleEventStreamContentType(response);

        await using var body = await response.Content.ReadAsStreamAsync(requestDeadline.Token);
        using var reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        var events = new List<SseEvent>();
        while (await reader.ReadLineAsync(requestDeadline.Token) is { } eventLine)
        {
            if (eventLine.Length == 0)
                continue;

            eventLine.Should().StartWith("event: ");
            var eventType = eventLine["event: ".Length..];
            var dataLine = await reader.ReadLineAsync(requestDeadline.Token);
            dataLine.Should().StartWith("data: ");
            (await reader.ReadLineAsync(requestDeadline.Token)).Should().BeEmpty();
            events.Add(new SseEvent(eventType, ParsePayload(dataLine["data: ".Length..])));
        }

        events.Select(e => e.EventType).Should().Equal("message.delta", "message.complete");
        events[0].Payload.GetProperty("token").GetString().Should().Be("buffered");
        events[0].Payload.GetProperty("isComplete").GetBoolean().Should().BeFalse();
        events[1].Payload.GetProperty("token").GetString().Should().Be(" response");
        events[1].Payload.GetProperty("isComplete").GetBoolean().Should().BeTrue();
    }

    private WebApplicationFactory<Program> CreateFactory(ILlmProvider provider) =>
        _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider>(_ => provider);
            });
        });

    private static async Task<ChatSessionDto> CreateSessionAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto(title));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();
        return session!;
    }

    private static void AssertSingleEventStreamContentType(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        response.Content.Headers.TryGetValues("Content-Type", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("text/event-stream");
    }

    private static async Task<SseEvent> ReadEventAsync(StreamReader reader)
    {
        var eventLine = await reader.ReadLineAsync().WaitAsync(ReadDeadline);
        eventLine.Should().StartWith("event: ");

        var dataLine = await reader.ReadLineAsync().WaitAsync(ReadDeadline);
        dataLine.Should().StartWith("data: ");

        var terminator = await reader.ReadLineAsync().WaitAsync(ReadDeadline);
        terminator.Should().BeEmpty("each SSE event must be terminated by a blank line");

        return new SseEvent(
            eventLine["event: ".Length..],
            ParsePayload(dataLine["data: ".Length..]));
    }

    private static JsonElement ParsePayload(string data) =>
        JsonSerializer.Deserialize<JsonElement>(data);

    private sealed record SseEvent(string EventType, JsonElement Payload);

    private abstract class OpenAiCompatibleProviderStub : ILlmProvider
    {
        public abstract IAsyncEnumerable<LlmTokenEvent> StreamAsync(
            ChatCompletionRequest request,
            CancellationToken ct = default);

        public Task<LlmCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new LlmCompletionResult(
                "unused",
                TokensUsed: 1,
                IsActionable: false,
                Provider: "OpenAI",
                Model: "gpt-4o-mini"));

        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new LlmHealthStatus(true, "OpenAI", Model: "gpt-4o-mini"));

        public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new LlmHealthStatus(true, "OpenAI", Model: "gpt-4o-mini", IsProbed: true));
    }

    private sealed class GatedOpenAiCompatibleProviderStub : OpenAiCompatibleProviderStub
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TerminalEventProduced { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public override async IAsyncEnumerable<LlmTokenEvent> StreamAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            yield return new LlmTokenEvent("first", false, Provider: "OpenAI", Model: "gpt-4o-mini");

            await _release.Task.WaitAsync(ct);
            TerminalEventProduced.TrySetResult();
            yield return new LlmTokenEvent(
                " complete",
                true,
                TokensUsed: 2,
                Provider: "OpenAI",
                Model: "gpt-4o-mini");
        }
    }

    private sealed class BufferedOpenAiCompatibleProviderStub : OpenAiCompatibleProviderStub
    {
        public override async IAsyncEnumerable<LlmTokenEvent> StreamAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            yield return new LlmTokenEvent("buffered", false, Provider: "OpenAI", Model: "gpt-4o-mini");
            await Task.CompletedTask;
            yield return new LlmTokenEvent(
                " response",
                true,
                TokensUsed: 2,
                Provider: "OpenAI",
                Model: "gpt-4o-mini");
        }
    }
}
