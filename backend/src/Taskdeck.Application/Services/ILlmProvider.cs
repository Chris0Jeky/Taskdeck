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
    double Temperature = 0.7,
    LlmRequestAttribution? Attribution = null
);

public record ChatCompletionMessage(string Role, string Content);

public enum LlmRequestSourceSurface
{
    Chat,
    Capture,
    Worker
}

public record LlmRequestAttribution(
    Guid UserId,
    string CorrelationId,
    LlmRequestSourceSurface SourceSurface,
    Guid? BoardId = null,
    Guid? SessionId = null
);

public record LlmCompletionResult(
    string Content,
    int TokensUsed,
    bool IsActionable,
    string? ActionIntent = null,
    string Provider = "Mock",
    string Model = "mock-default"
);

public record LlmTokenEvent(string Token, bool IsComplete);

public record LlmHealthStatus(bool IsAvailable, string ProviderName, string? ErrorMessage = null, string? Model = null);
