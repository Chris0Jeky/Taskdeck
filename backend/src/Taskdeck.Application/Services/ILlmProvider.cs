using System.Text.Json;

namespace Taskdeck.Application.Services;

public interface ILlmProvider
{
    Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default);
    IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, CancellationToken ct = default);
    Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default);
    Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a chat completion request with tool schemas, allowing the LLM to call tools.
    /// Default implementation throws <see cref="NotSupportedException"/>.
    /// </summary>
    Task<LlmToolCompletionResult> CompleteWithToolsAsync(
        ChatCompletionRequest request,
        IReadOnlyList<TaskdeckToolSchema> tools,
        IReadOnlyList<ToolCallResult>? previousToolResults = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"{GetType().Name} does not support tool calling.");
    }
}

public record ChatCompletionRequest(
    List<ChatCompletionMessage> Messages,
    int MaxTokens = 2048,
    double Temperature = 0.7,
    LlmRequestAttribution? Attribution = null,
    string? SystemPrompt = null,
    string? BoardContext = null
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
    string Model = "mock-default",
    bool IsDegraded = false,
    string? DegradedReason = null,
    List<string>? Instructions = null
);

public record LlmTokenEvent(
    string Token,
    bool IsComplete,
    string? Error = null,
    int? TokensUsed = null,
    string? Provider = null,
    string? Model = null);

public record LlmHealthStatus(
    bool IsAvailable,
    string ProviderName,
    string? ErrorMessage = null,
    string? Model = null,
    bool IsMock = false,
    bool IsProbed = false);

// ── Tool-calling types ──────────────────────────────────────────────

/// <summary>
/// Provider-agnostic tool schema. Defined once in the Application layer and
/// converted to provider-specific wire format at the boundary.
/// </summary>
public record TaskdeckToolSchema(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    IReadOnlyList<string> Required
);

/// <summary>
/// A tool call request returned by the LLM provider (provider-assigned call ID).
/// </summary>
public record ToolCallRequest(
    string CallId,
    string ToolName,
    JsonElement Arguments
);

/// <summary>
/// The result of executing a tool, sent back to the LLM in a subsequent round.
/// Carries the original <see cref="Arguments"/> so providers can replay them
/// in synthetic assistant messages without losing fidelity.
/// </summary>
public record ToolCallResult(
    string CallId,
    string ToolName,
    string Content,
    bool IsError,
    JsonElement Arguments = default
);

/// <summary>
/// The result of a tool-calling-aware completion request.
/// When <see cref="IsComplete"/> is false, <see cref="ToolCalls"/> contains
/// pending tool invocations the orchestrator must execute.
/// </summary>
public record LlmToolCompletionResult(
    string? Content,
    int TokensUsed,
    string Provider,
    string Model,
    IReadOnlyList<ToolCallRequest>? ToolCalls,
    bool IsComplete,
    bool IsDegraded = false,
    string? DegradedReason = null
);
