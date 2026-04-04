using System.Text.Json;
using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Edge case tests for LLM provider abstraction boundaries.
/// Covers: default CompleteWithToolsAsync throws NotSupportedException,
/// MockLlmProvider edge cases (empty messages, null, tool-calling patterns),
/// provider empty/degraded response handling, provider selection policy
/// edge cases, and kill switch settings validation.
/// </summary>
public class LlmProviderAbstractionEdgeCaseTests
{
    // ── ILlmProvider default interface implementation ──────────────

    [Fact]
    public async Task DefaultCompleteWithToolsAsync_ThrowsNotSupportedException()
    {
        // A provider that does NOT override CompleteWithToolsAsync should throw.
        // Must be called through the interface to access the default implementation.
        ILlmProvider provider = new MinimalTestProvider();

        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "test") });
        var tools = Array.Empty<TaskdeckToolSchema>();

        var act = async () => await provider.CompleteWithToolsAsync(request, tools);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*MinimalTestProvider*");
    }

    // ── MockLlmProvider CompleteAsync edge cases ──────────────────

    [Fact]
    public async Task MockProvider_CompleteAsync_EmptyMessageList_DoesNotThrow()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(new List<ChatCompletionMessage>());

        var result = await provider.CompleteAsync(request);

        result.Should().NotBeNull();
        result.Provider.Should().Be("Mock");
        result.Content.Should().NotBeNull();
    }

    [Fact]
    public async Task MockProvider_CompleteAsync_NonUserRoleOnly_FallsBackToEmpty()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("System", "You are a helpful assistant.") });

        var result = await provider.CompleteAsync(request);

        result.Should().NotBeNull();
        result.IsActionable.Should().BeFalse(); // Empty user message should not be actionable
    }

    [Fact]
    public async Task MockProvider_CompleteAsync_ActionableMessage_ReturnsInstructions()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "create a new card called Test Task") });

        var result = await provider.CompleteAsync(request);

        result.IsActionable.Should().BeTrue();
        result.ActionIntent.Should().NotBeNullOrEmpty();
        result.Content.Should().Contain("proposal");
    }

    [Fact]
    public async Task MockProvider_CompleteAsync_VeryLongInput_DoesNotThrow()
    {
        var provider = new MockLlmProvider();
        var longInput = new string('a', 10000);
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", longInput) });

        var act = async () => await provider.CompleteAsync(request);

        await act.Should().NotThrowAsync();
        var result = await provider.CompleteAsync(request);
        result.TokensUsed.Should().BeGreaterThan(0);
    }

    // ── MockLlmProvider CompleteWithToolsAsync edge cases ────────

    [Fact]
    public async Task MockProvider_ToolCalling_EmptyMessages_ReturnsFinalResponse()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(new List<ChatCompletionMessage>());
        var tools = Array.Empty<TaskdeckToolSchema>();

        var result = await provider.CompleteWithToolsAsync(request, tools);

        result.IsComplete.Should().BeTrue();
        result.ToolCalls.Should().BeNull();
        result.Provider.Should().Be("Mock");
        result.Model.Should().Be("mock-tool-v1");
    }

    [Fact]
    public async Task MockProvider_ToolCalling_PreviousResults_ContainsToolNameInSummary()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "What cards?") });

        var previousResults = new List<ToolCallResult>
        {
            new("call-1", "list_cards_in_column", "{\"cards\":[]}", false),
            new("call-2", "get_board_labels", "{\"labels\":[]}", false)
        };

        var result = await provider.CompleteWithToolsAsync(request, Array.Empty<TaskdeckToolSchema>(), previousResults);

        result.IsComplete.Should().BeTrue();
        result.Content.Should().Contain("list_cards_in_column");
        result.Content.Should().Contain("get_board_labels");
    }

    [Fact]
    public async Task MockProvider_ToolCalling_PreviousResultsWithError_StillReturnsSummary()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "Show cards") });

        var previousResults = new List<ToolCallResult>
        {
            new("call-1", "get_card_details", "{\"error\":\"not found\"}", true)
        };

        var result = await provider.CompleteWithToolsAsync(request, Array.Empty<TaskdeckToolSchema>(), previousResults);

        result.IsComplete.Should().BeTrue();
        result.Content.Should().Contain("get_card_details");
    }

    [Fact]
    public async Task MockProvider_ToolCalling_VeryLongToolResult_IsTruncatedInSummary()
    {
        var provider = new MockLlmProvider();
        var request = new ChatCompletionRequest(
            new List<ChatCompletionMessage> { new("User", "Show cards") });

        var longResult = new string('Z', 500);
        var previousResults = new List<ToolCallResult>
        {
            new("call-1", "list_cards_in_column", longResult, false)
        };

        var result = await provider.CompleteWithToolsAsync(request, Array.Empty<TaskdeckToolSchema>(), previousResults);

        result.IsComplete.Should().BeTrue();
        result.Content.Should().NotBeNull();
        // The MockLlmProvider truncates tool results at 200 chars.
        // The full 500-char 'Z' string should NOT appear in the summary.
        result.Content!.Should().NotContain(longResult,
            "tool result should be truncated in the summary, not included verbatim");
        // But the truncated version (first 200 chars) should appear
        result.Content.Should().Contain(new string('Z', 200));
    }

    // ── MockLlmProvider Health ──────────────────────────────────

    [Fact]
    public async Task MockProvider_GetHealthAsync_ReturnsIsMockTrue()
    {
        var provider = new MockLlmProvider();

        var health = await provider.GetHealthAsync();

        health.IsAvailable.Should().BeTrue();
        health.IsMock.Should().BeTrue();
        health.ProviderName.Should().Be("Mock");
        health.IsProbed.Should().BeFalse();
    }

    [Fact]
    public async Task MockProvider_ProbeAsync_ReturnsIsProbedTrue()
    {
        var provider = new MockLlmProvider();

        var health = await provider.ProbeAsync();

        health.IsAvailable.Should().BeTrue();
        health.IsMock.Should().BeTrue();
        health.IsProbed.Should().BeTrue();
    }

    // ── Provider selection edge cases ───────────────────────────

    [Fact]
    public void ProviderSelection_NullSettings_FallsBackToMock()
    {
        var settings = new LlmProviderSettings();

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
    }

    [Fact]
    public void ProviderSelection_EmptyProviderString_FallsBackToMock()
    {
        var settings = new LlmProviderSettings
        {
            EnableLiveProviders = true,
            Provider = ""
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
    }

    [Fact]
    public void ProviderSelection_CaseInsensitive_OpenAI()
    {
        var settings = new LlmProviderSettings
        {
            EnableLiveProviders = true,
            Provider = "openai", // lowercase
            OpenAi = new OpenAiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                TimeoutSeconds = 30
            }
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.OpenAi);
    }

    // ── LlmCompletionResult record behavior ─────────────────────

    [Fact]
    public void LlmCompletionResult_DefaultValues_AreSensible()
    {
        var result = new LlmCompletionResult("test content", 10, false);

        result.Provider.Should().Be("Mock");
        result.Model.Should().Be("mock-default");
        result.IsDegraded.Should().BeFalse();
        result.DegradedReason.Should().BeNull();
        result.Instructions.Should().BeNull();
    }

    [Fact]
    public void LlmToolCompletionResult_DegradedWithContent_IsAccessible()
    {
        var result = new LlmToolCompletionResult(
            Content: "Degraded response",
            TokensUsed: 0,
            Provider: "OpenAI",
            Model: "gpt-4o-mini",
            ToolCalls: null,
            IsComplete: true,
            IsDegraded: true,
            DegradedReason: "Rate limited");

        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Be("Rate limited");
        result.Content.Should().Be("Degraded response");
        result.IsComplete.Should().BeTrue();
    }

    // ── Kill switch settings ─────────────────────────────────────

    [Fact]
    public void KillSwitchSettings_DefaultValues_AreInactive()
    {
        var settings = new LlmKillSwitchSettings();

        settings.GlobalKill.Should().BeFalse();
        settings.KilledSurfaces.Should().BeEmpty();
        settings.KilledUserIds.Should().BeEmpty();
    }

    /// <summary>
    /// Minimal ILlmProvider implementation that does NOT override CompleteWithToolsAsync,
    /// ensuring the default interface method (which throws) is tested.
    /// </summary>
    private class MinimalTestProvider : ILlmProvider
    {
        public Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
            => Task.FromResult(new LlmCompletionResult("test", 0, false));

        public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, CancellationToken ct = default)
        {
            yield return new LlmTokenEvent("test", true);
            await Task.CompletedTask;
        }

        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmHealthStatus(true, "MinimalTest"));

        public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmHealthStatus(true, "MinimalTest", IsProbed: true));
    }
}
