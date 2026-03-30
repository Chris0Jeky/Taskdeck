using System.Runtime.CompilerServices;

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
}
