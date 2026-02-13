namespace Taskdeck.Application.Services;

public interface ILlmProvider
{
    Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default);
    IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, CancellationToken ct = default);
    Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default);
}

public record ChatCompletionRequest(
    List<ChatCompletionMessage> Messages,
    int MaxTokens = 1024,
    double Temperature = 0.7
);

public record ChatCompletionMessage(string Role, string Content);

public record LlmCompletionResult(
    string Content,
    int TokensUsed,
    bool IsActionable,
    string? ActionIntent = null
);

public record LlmTokenEvent(string Token, bool IsComplete);

public record LlmHealthStatus(bool IsAvailable, string ProviderName, string? ErrorMessage = null);
