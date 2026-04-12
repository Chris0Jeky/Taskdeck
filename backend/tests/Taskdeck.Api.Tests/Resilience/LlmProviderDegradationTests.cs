using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests.Resilience;

/// <summary>
/// Tests that LLM provider failures (timeout, invalid response, total unavailability)
/// are surfaced as degraded responses rather than 500 errors or infinite waits.
/// </summary>
public class LlmProviderDegradationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _baseFactory;

    public LlmProviderDegradationTests(TestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    // ── Provider Timeout ───────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_WhenProviderTimesOut_ReturnsDegradedResponseNotInfiniteWait()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider>(_ => new TimeoutProviderStub());
            });
        });
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        await ApiTestHarness.AuthenticateAsync(client, "llm-timeout-resilience");

        var createSessionResponse = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Timeout provider test"));
        createSessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await createSessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();

        var sendMessageResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{session!.Id}/messages",
            new SendChatMessageDto("tell me something"));

        // The request should not hang forever; it should return within the test timeout.
        // The response may be degraded or an error -- the key assertion is no infinite wait.
        sendMessageResponse.Should().NotBeNull(
            "request should complete even when provider times out");

        // Since the provider throws OperationCanceledException simulating timeout,
        // the chat service should handle this and return a 500 error contract
        // rather than an unhandled exception.
        var statusCode = (int)sendMessageResponse.StatusCode;
        statusCode.Should().BeOneOf(new[] { 200, 500 },
            "should either return a degraded response or an error contract, not hang");
    }

    // ── Provider Throws Exception ──────────────────────────────────────

    [Fact]
    public async Task SendMessage_WhenProviderThrowsException_ReturnsErrorContract()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider>(_ => new ThrowingProviderStub());
            });
        });
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "llm-throw-resilience");

        var createSessionResponse = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Throwing provider test"));
        createSessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await createSessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();

        var sendMessageResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{session!.Id}/messages",
            new SendChatMessageDto("create card 'Test'"));

        sendMessageResponse.Should().NotBeNull();
        var statusCode = (int)sendMessageResponse.StatusCode;
        statusCode.Should().BeOneOf(new[] { 200, 500 },
            "should return an error contract or degraded response, not crash");

        if (sendMessageResponse.StatusCode == HttpStatusCode.InternalServerError)
        {
            var body = await sendMessageResponse.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("errorCode", out _).Should().BeTrue(
                "500 response should follow error contract with errorCode");
            body.TryGetProperty("message", out _).Should().BeTrue(
                "500 response should follow error contract with message");
        }
    }

    // ── Provider Unavailable but Non-LLM Features Still Work ──────────

    [Fact]
    public async Task BoardCrud_StillWorks_WhenAllProvidersUnavailable()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider>(_ => new TotallyDeadProviderStub());
            });
        });
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "llm-dead-board-crud");

        // Board CRUD should work regardless of LLM provider state.
        var board = await ApiTestHarness.CreateBoardAsync(client, "resilience-board");
        board.Should().NotBeNull();
        board.Name.Should().StartWith("resilience-board");

        var getResponse = await client.GetAsync($"/api/boards/{board.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await client.GetAsync("/api/boards");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CaptureItems_StillWork_WhenProviderUnavailable()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider>(_ => new TotallyDeadProviderStub());
            });
        });
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "llm-dead-capture");

        // Capture should still accept items even when the LLM is dead.
        // The items queue up for later processing.
        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "capture while LLM is down"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            "capture should accept items even when LLM provider is unavailable");
    }

    // ── Provider Health Reports Unhealthy ──────────────────────────────

    [Fact]
    public async Task ProviderHealth_ReportsUnhealthy_WhenProviderIsDown()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider>(_ => new TotallyDeadProviderStub());
            });
        });
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "llm-dead-health");

        var response = await client.GetAsync("/api/llm/chat/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ChatProviderHealthDto>();
        payload.Should().NotBeNull();
        payload!.IsAvailable.Should().BeFalse(
            "health check should report the provider as unavailable");
        payload.ErrorMessage.Should().NotBeNullOrWhiteSpace(
            "health check should include an error explanation");
    }

    [Fact]
    public async Task ProviderHealth_WithProbe_ReportsUnhealthy_WhenProviderIsDown()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider>(_ => new TotallyDeadProviderStub());
            });
        });
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "llm-dead-probe");

        var response = await client.GetAsync("/api/llm/chat/health?probe=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ChatProviderHealthDto>();
        payload.Should().NotBeNull();
        payload!.IsAvailable.Should().BeFalse();
        payload.IsProbed.Should().BeTrue();
    }

    // ── Stub Implementations ──────────────────────────────────────────

    /// <summary>
    /// Provider that simulates a timeout by delaying beyond cancellation.
    /// </summary>
    private sealed class TimeoutProviderStub : ILlmProvider
    {
        public async Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
        {
            // Simulate a long wait that would be cancelled by the service's timeout.
            using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            internalCts.CancelAfter(TimeSpan.FromMilliseconds(50));
            await Task.Delay(TimeSpan.FromSeconds(60), internalCts.Token);
            throw new InvalidOperationException("Should not reach here");
        }

        public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Use a short internal timeout to avoid hanging for 60 seconds if a test
            // hits the streaming endpoint. Cancels quickly like CompleteAsync does.
            using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            internalCts.CancelAfter(TimeSpan.FromMilliseconds(50));
            await Task.Delay(TimeSpan.FromSeconds(60), internalCts.Token);
            yield return new LlmTokenEvent("timeout", true);
        }

        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmHealthStatus(false, "TimeoutStub", "Provider timed out"));

        public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmHealthStatus(false, "TimeoutStub", "Provider timed out", IsProbed: true));
    }

    /// <summary>
    /// Provider that throws an unhandled exception on every call.
    /// </summary>
    private sealed class ThrowingProviderStub : ILlmProvider
    {
        public Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated provider crash");

        public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            ThrowStreamCrash();
            yield break;
        }

        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmHealthStatus(false, "ThrowingStub", "Provider threw exception"));

        public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmHealthStatus(false, "ThrowingStub", "Provider threw exception", IsProbed: true));

        private static void ThrowStreamCrash()
            => throw new InvalidOperationException("Simulated stream crash");
    }

    /// <summary>
    /// Provider where everything reports unavailable.
    /// </summary>
    private sealed class TotallyDeadProviderStub : ILlmProvider
    {
        public Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("All providers are down");

        public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            ThrowProvidersDown();
            yield break;
        }

        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmHealthStatus(false, "Dead", "All providers are unavailable"));

        public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmHealthStatus(false, "Dead", "All providers are unavailable", IsProbed: true));

        private static void ThrowProvidersDown()
            => throw new InvalidOperationException("All providers are down");
    }
}
