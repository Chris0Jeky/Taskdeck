using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ChatApiLiveProviderStubTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _baseFactory;

    public ChatApiLiveProviderStubTests(TestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task SendMessage_ShouldUseNonMockProviderStub_ForChatFlow()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped<ILlmProvider, OpenAiProviderStub>();
            });
        });
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "chat-live-provider-stub");

        var createSessionResponse = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Live provider stub flow"));
        createSessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await createSessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();

        var sendMessageResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{session!.Id}/messages",
            new SendChatMessageDto("create card from this instruction"));

        sendMessageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var assistant = await sendMessageResponse.Content.ReadFromJsonAsync<ChatMessageDto>();
        assistant.Should().NotBeNull();
        assistant!.MessageType.Should().Be("status");
        assistant.Content.Should().Contain("OpenAI stub");
        assistant.TokenUsage.Should().Be(123);
    }

    private sealed class OpenAiProviderStub : ILlmProvider
    {
        public Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new LlmCompletionResult(
                Content: "OpenAI stub completion for integration test.",
                TokensUsed: 123,
                IsActionable: true,
                ActionIntent: "card.create",
                Provider: "OpenAI",
                Model: "gpt-4o-mini"));
        }

        public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            yield return new LlmTokenEvent("OpenAI", false);
            await Task.Delay(1, ct);
            ct.ThrowIfCancellationRequested();
            yield return new LlmTokenEvent(" stub", true);
        }

        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new LlmHealthStatus(
                IsAvailable: true,
                ProviderName: "OpenAI",
                Model: "gpt-4o-mini"));
        }
    }
}
