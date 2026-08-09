using System.Text.Json;
using System.Text.Json.Serialization;

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
)
{
    /// <summary>
    /// Per-request transport participation state. Providers that support the
    /// dispatch boundary observe this context before validation and mark it only
    /// when the request reaches the innermost HTTP transport handler.
    /// </summary>
    [JsonIgnore]
    internal LlmDispatchContext DispatchContext { get; init; } = new();
}

public record ChatCompletionMessage(string Role, string Content);

// Serialised via ToString().ToLowerInvariant() — values must match IsSupportedSourceSurface allowlist.
// VsCode (not VsCodeExtension) is intentional: serialises to "vscode" matching the source surface string.
public enum LlmRequestSourceSurface
{
    Chat,
    Capture,
    Worker,
    VsCode
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
    List<string>? Instructions = null,
    bool IsClarificationRequest = false
)
{
    /// <summary>
    /// True only when <see cref="TokensUsed"/> came from authoritative upstream
    /// usage metadata. A false value tells quota consumers to settle the reserved
    /// estimate instead of treating a local output-only estimate as total usage.
    /// </summary>
    [JsonIgnore]
    public bool HasAuthoritativeTokenUsage { get; init; } = true;

    // Internal billing/circuit metadata must not become part of the API wire shape.
    // Providers set this false when selection/configuration rejected a request before
    // any upstream dispatch, so quota callers can release rather than charge.
    [JsonIgnore]
    internal bool ShouldSettleQuotaReservation { get; init; } = true;

    [JsonIgnore]
    internal LlmProviderFailureKind ProviderFailureKind { get; init; }

    [JsonIgnore]
    internal bool CountsAsProviderFailure => ProviderFailureKind != LlmProviderFailureKind.None;
}

public record LlmTokenEvent(
    string Token,
    bool IsComplete,
    string? Error = null,
    int? TokensUsed = null,
    string? Provider = null,
    string? Model = null)
{
    // Keep these out of the positional constructor. LlmTokenEvent is part of the
    // public Application contract, so extending its existing constructor would
    // break already-compiled consumers even though in-tree source still builds.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDegraded { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DegradedReason { get; init; }

    [JsonIgnore]
    internal LlmProviderFailureKind ProviderFailureKind { get; init; }

    [JsonIgnore]
    internal bool CountsAsProviderFailure => ProviderFailureKind != LlmProviderFailureKind.None;
}

internal enum LlmProviderFailureKind
{
    None = 0,
    Transport = 1,
    ResponseBody = 2,
    Protocol = 3,
    Timeout = 4,
    ResponseLimit = 5
}

internal enum LlmDispatchPhase
{
    Unobserved = 0,
    ObservedPreDispatch = 1,
    Dispatched = 2
}

internal readonly record struct LlmDispatchSnapshot(
    LlmDispatchPhase Phase,
    string? Provider,
    string? Model);

/// <summary>
/// Monotonic request-scoped state shared by the quota owner and HTTP pipeline.
/// A lock keeps provider/model identity and the phase visible as one snapshot.
/// </summary>
internal sealed class LlmDispatchContext
{
    private readonly object _gate = new();
    private LlmDispatchPhase _phase;
    private string? _provider;
    private string? _model;

    public void Observe(string provider, string model)
    {
        lock (_gate)
        {
            if (_phase != LlmDispatchPhase.Unobserved)
                return;

            _provider = provider;
            _model = model;
            _phase = LlmDispatchPhase.ObservedPreDispatch;
        }
    }

    public void MarkDispatched()
    {
        lock (_gate)
        {
            if (_phase == LlmDispatchPhase.ObservedPreDispatch)
                _phase = LlmDispatchPhase.Dispatched;
        }
    }

    public LlmDispatchSnapshot ReadSnapshot()
    {
        lock (_gate)
        {
            return new LlmDispatchSnapshot(_phase, _provider, _model);
        }
    }
}

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
