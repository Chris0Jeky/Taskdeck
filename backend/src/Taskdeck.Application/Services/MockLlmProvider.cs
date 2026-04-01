using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Taskdeck.Application.Services;

public class MockLlmProvider : ILlmProvider
{
    public Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
    {
        var lastUserMessage = request.Messages
            .LastOrDefault(m => m.Role.Equals("User", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? "";

        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(lastUserMessage);

        // When actionable intent is detected, attempt to extract structured
        // instructions from the natural language message. This bridges the gap
        // between classification ("this is a card.create request") and parsing
        // ("create card 'title'"). Without this, natural language like "create
        // new onboarding tasks" would be passed raw to the regex parser and fail.
        List<string>? instructions = null;
        if (isActionable)
        {
            var extracted = NaturalLanguageInstructionExtractor.Extract(lastUserMessage, actionIntent);
            if (extracted.Count > 0)
                instructions = extracted;
        }

        var content = isActionable
            ? $"I can help with that. I'll create a proposal to {actionIntent}."
            : $"Here's information about your request: {lastUserMessage.Trim()}";

        return Task.FromResult(new LlmCompletionResult(
            content,
            TokensUsed: lastUserMessage.Length / 4 + content.Length / 4,
            IsActionable: isActionable,
            ActionIntent: actionIntent,
            Provider: "Mock",
            Model: "mock-default",
            Instructions: instructions
        ));
    }

    public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await CompleteAsync(request, ct);
        var words = result.Content.Split(' ');

        for (int i = 0; i < words.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var token = (i == 0 ? "" : " ") + words[i];
            yield return new LlmTokenEvent(token, i == words.Length - 1);
            await Task.Delay(50, ct);
        }
    }

    public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new LlmHealthStatus(true, "Mock", Model: "mock-default", IsMock: true));
    }

    public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new LlmHealthStatus(true, "Mock", Model: "mock-default", IsMock: true, IsProbed: true));
    }

    public Task<LlmToolCompletionResult> CompleteWithToolsAsync(
        ChatCompletionRequest request,
        IReadOnlyList<TaskdeckToolSchema> tools,
        IReadOnlyList<ToolCallResult>? previousToolResults = null,
        CancellationToken ct = default)
    {
        // If we have previous tool results, this is a follow-up round.
        // Return a final text response summarizing the tool results.
        if (previousToolResults is { Count: > 0 })
        {
            var summaryParts = new List<string>();
            foreach (var result in previousToolResults)
            {
                summaryParts.Add($"[{result.ToolName}]: {TruncateForSummary(result.Content)}");
            }

            var summary = string.Join("; ", summaryParts);
            var content = $"Based on the tool results, here is what I found: {summary}";

            return Task.FromResult(new LlmToolCompletionResult(
                Content: content,
                TokensUsed: content.Length / 4,
                Provider: "Mock",
                Model: "mock-tool-v1",
                ToolCalls: null,
                IsComplete: true));
        }

        // First round: try to match user message to a tool call
        var lastUserMessage = request.Messages
            .LastOrDefault(m => m.Role.Equals("User", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? "";

        var dispatched = MockToolCallDispatcher.TryDispatch(lastUserMessage);
        if (dispatched != null)
        {
            return Task.FromResult(new LlmToolCompletionResult(
                Content: null,
                TokensUsed: lastUserMessage.Length / 4 + 30,
                Provider: "Mock",
                Model: "mock-tool-v1",
                ToolCalls: new[] { dispatched },
                IsComplete: false));
        }

        // No tool match — return a plain text response
        var textContent = $"Here's information about your request: {lastUserMessage.Trim()}";
        return Task.FromResult(new LlmToolCompletionResult(
            Content: textContent,
            TokensUsed: textContent.Length / 4,
            Provider: "Mock",
            Model: "mock-tool-v1",
            ToolCalls: null,
            IsComplete: true));
    }

    private static string TruncateForSummary(string content)
    {
        const int maxLength = 200;
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
        return content[..maxLength] + "...";
    }
}
