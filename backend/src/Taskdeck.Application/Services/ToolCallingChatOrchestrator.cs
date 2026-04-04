using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Services.Tools;

namespace Taskdeck.Application.Services;

/// <summary>
/// Orchestrates multi-turn tool-calling conversations between the LLM and tool executors.
/// Wraps the provider's <see cref="ILlmProvider.CompleteWithToolsAsync"/> in a loop that:
/// 1. Sends the user message with tool schemas
/// 2. Executes any tool calls returned by the LLM
/// 3. Feeds tool results back to the LLM
/// 4. Repeats until the LLM returns a final text response or limits are reached
/// </summary>
public sealed class ToolCallingChatOrchestrator
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    /// <summary>Maximum tool-calling rounds per user message.</summary>
    public const int MaxRounds = 5;

    /// <summary>Total orchestration timeout in seconds.</summary>
    public const int TotalTimeoutSeconds = 60;

    /// <summary>Per-round timeout in seconds for a single LLM API call.</summary>
    public const int PerRoundTimeoutSeconds = 30;

    private readonly ILlmProvider _provider;
    private readonly ToolExecutorRegistry _executorRegistry;
    private readonly ILogger<ToolCallingChatOrchestrator> _logger;
    private readonly IToolStatusNotifier? _statusNotifier;
    private readonly LlmToolCallingSettings _settings;

    public ToolCallingChatOrchestrator(
        ILlmProvider provider,
        ToolExecutorRegistry executorRegistry,
        ILogger<ToolCallingChatOrchestrator> logger,
        IToolStatusNotifier? statusNotifier = null,
        LlmToolCallingSettings? settings = null)
    {
        _provider = provider;
        _executorRegistry = executorRegistry;
        _logger = logger;
        _statusNotifier = statusNotifier;
        _settings = settings ?? new LlmToolCallingSettings();
    }

    /// <summary>
    /// Executes a multi-turn tool-calling conversation for the given request.
    /// Returns the final result including accumulated token usage and tool call metadata.
    /// </summary>
    public async Task<ToolCallingResult> ExecuteAsync(
        ChatCompletionRequest request,
        Guid boardId,
        CancellationToken ct = default)
    {
        return await ExecuteAsync(request, boardId, Guid.Empty, ct);
    }

    /// <summary>
    /// Executes a multi-turn tool-calling conversation with user context for write tools.
    /// Returns the final result including accumulated token usage and tool call metadata.
    /// </summary>
    public async Task<ToolCallingResult> ExecuteAsync(
        ChatCompletionRequest request,
        Guid boardId,
        Guid userId,
        CancellationToken ct = default)
    {
        var tools = ReadToolSchemas.GetAll()
            .Concat(WriteToolSchemas.GetAll())
            .ToList();
        var totalTokensUsed = 0;
        var toolCallLog = new List<ToolCallLogEntry>();
        var stopwatch = Stopwatch.StartNew();
        string provider = "Unknown";
        string model = "unknown";

        IReadOnlyList<ToolCallResult>? previousResults = null;
        string? previousRoundFingerprint = null;
        bool previousRoundHadErrors = false;

        for (var round = 1; round <= MaxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            // Check total timeout
            if (stopwatch.Elapsed.TotalSeconds > TotalTimeoutSeconds)
            {
                _logger.LogWarning("Tool-calling orchestration exceeded total timeout of {Timeout}s at round {Round}.",
                    TotalTimeoutSeconds, round);
                return BuildTimeoutResult(totalTokensUsed, toolCallLog, provider, model);
            }

            LlmToolCompletionResult llmResult;
            try
            {
                using var roundCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                roundCts.CancelAfter(TimeSpan.FromSeconds(PerRoundTimeoutSeconds));

                llmResult = await _provider.CompleteWithToolsAsync(
                    request, tools, previousResults, roundCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Tool-calling round {Round} timed out after {Timeout}s.",
                    round, PerRoundTimeoutSeconds);
                return BuildTimeoutResult(totalTokensUsed, toolCallLog, provider, model);
            }
            catch (NotSupportedException)
            {
                _logger.LogWarning("Provider does not support tool calling; falling back to single-turn.");
                return BuildDegradedResult(provider, model, totalTokensUsed, round, toolCallLog,
                    "Provider does not support tool calling.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Tool-calling round {Round} failed. {ExceptionSummary}",
                    round, SensitiveDataRedactor.SummarizeException(ex));
                return BuildDegradedResult(provider, model, totalTokensUsed, round, toolCallLog,
                    "Tool-calling round failed due to an internal error.");
            }

            totalTokensUsed += llmResult.TokensUsed;
            provider = llmResult.Provider;
            model = llmResult.Model;

            // If the LLM returned a degraded result, return it immediately
            if (llmResult.IsDegraded)
            {
                return new ToolCallingResult(
                    Content: llmResult.Content ?? "I encountered an issue processing your request.",
                    TokensUsed: totalTokensUsed,
                    Provider: provider,
                    Model: model,
                    Rounds: round,
                    ToolCallLog: toolCallLog,
                    IsDegraded: true,
                    DegradedReason: llmResult.DegradedReason);
            }

            // If the response is complete (no tool calls), we're done
            if (llmResult.IsComplete)
            {
                return new ToolCallingResult(
                    Content: llmResult.Content ?? "",
                    TokensUsed: totalTokensUsed,
                    Provider: provider,
                    Model: model,
                    Rounds: round,
                    ToolCallLog: toolCallLog);
            }

            // Process tool calls
            if (llmResult.ToolCalls is not { Count: > 0 })
            {
                _logger.LogWarning("LLM returned incomplete result with no tool calls at round {Round}.", round);
                return new ToolCallingResult(
                    Content: "I was unable to complete your request.",
                    TokensUsed: totalTokensUsed,
                    Provider: provider,
                    Model: model,
                    Rounds: round,
                    ToolCallLog: toolCallLog,
                    IsDegraded: true,
                    DegradedReason: "Unexpected empty tool call list.");
            }

            // Infinite loop detection: abort if the LLM issues the exact same
            // tool calls (name + arguments) as the previous round.
            // Skip detection when the previous round had errors — the LLM may
            // legitimately retry the same call after a transient tool failure.
            var currentFingerprint = ComputeToolCallFingerprint(llmResult.ToolCalls);
            if (previousRoundFingerprint != null &&
                !previousRoundHadErrors &&
                string.Equals(currentFingerprint, previousRoundFingerprint, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Tool-calling loop detected at round {Round}: identical tool calls as previous round. Aborting.",
                    round);
                return BuildLoopDetectedResult(totalTokensUsed, toolCallLog, provider, model, round);
            }
            previousRoundFingerprint = currentFingerprint;

            var results = new List<ToolCallResult>();
            foreach (var toolCall in llmResult.ToolCalls)
            {
                // Send status notification
                if (_statusNotifier != null)
                {
                    var displayMessage = BuildStatusMessage(toolCall.ToolName, toolCall.Arguments);
                    await _statusNotifier.NotifyToolStatusAsync(
                        boardId, toolCall.ToolName, displayMessage, round, MaxRounds, ct);
                }

                var executor = _executorRegistry.GetExecutor(toolCall.ToolName);
                string resultContent;
                bool isError;

                if (executor == null)
                {
                    _logger.LogWarning("No executor found for tool '{ToolName}'.", toolCall.ToolName);
                    resultContent = JsonSerializer.Serialize(new
                    {
                        error = $"Unknown tool: {toolCall.ToolName}",
                        suggestion = "Available tools: " + string.Join(", ", _executorRegistry.GetRegisteredToolNames())
                    });
                    isError = true;
                }
                else
                {
                    try
                    {
                        var context = new ToolExecutionContext(boardId, userId);
                        resultContent = await executor.ExecuteAsync(context, toolCall.Arguments, ct);
                        isError = false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Tool '{ToolName}' execution failed. {ExceptionSummary}",
                            toolCall.ToolName, SensitiveDataRedactor.SummarizeException(ex));
                        resultContent = JsonSerializer.Serialize(new
                        {
                            error = $"Tool execution failed: {toolCall.ToolName}",
                            message = "An internal error occurred while executing this tool."
                        });
                        isError = true;
                    }
                }

                // Enforce token budget: truncate oversized tool results before they
                // are fed back to the LLM.  This keeps the conversation within the
                // provider's context window even when a tool returns a large payload.
                resultContent = TruncateToolResult(resultContent, _settings.MaxToolResultBytes);

                results.Add(new ToolCallResult(
                    toolCall.CallId, toolCall.ToolName, resultContent, isError));

                toolCallLog.Add(new ToolCallLogEntry(
                    round, toolCall.ToolName, toolCall.Arguments,
                    TruncateForLog(resultContent), isError));
            }

            previousResults = results;
            previousRoundHadErrors = results.Any(r => r.IsError);
        }

        // Exhausted all rounds without a final response
        _logger.LogWarning("Tool-calling exhausted {MaxRounds} rounds without final response.", MaxRounds);
        return BuildExhaustedResult(totalTokensUsed, toolCallLog, provider, model);
    }

    private static string BuildStatusMessage(string toolName, JsonElement arguments)
    {
        return toolName switch
        {
            // Read tools
            "list_board_columns" => "Looking up board columns...",
            "list_cards_in_column" when arguments.TryGetProperty("column_name", out var cn) =>
                $"Looking up cards in {cn.GetString()}...",
            "list_cards_in_column" => "Looking up cards...",
            "get_card_details" when arguments.TryGetProperty("card_id", out var ci) =>
                $"Getting details for card {ci.GetString()}...",
            "get_card_details" => "Getting card details...",
            "search_cards" when arguments.TryGetProperty("query", out var q) =>
                $"Searching for \"{q.GetString()}\"...",
            "search_cards" => "Searching cards...",
            "get_board_labels" => "Looking up board labels...",
            // Write tools (always proposals)
            "propose_create_card" when arguments.TryGetProperty("title", out var title) =>
                $"Creating proposal for new card \"{title.GetString()}\"...",
            "propose_create_card" => "Creating proposal for new card...",
            "propose_move_card" when arguments.TryGetProperty("card_id", out var mc) =>
                $"Creating proposal to move card {mc.GetString()}...",
            "propose_move_card" => "Creating proposal to move card...",
            "propose_archive_card" when arguments.TryGetProperty("card_id", out var ac) =>
                $"Creating proposal to archive card {ac.GetString()}...",
            "propose_archive_card" => "Creating proposal to archive card...",
            "propose_update_card" when arguments.TryGetProperty("card_id", out var uc) =>
                $"Creating proposal to update card {uc.GetString()}...",
            "propose_update_card" => "Creating proposal to update card...",
            "propose_bulk_move" when arguments.TryGetProperty("source_column", out var bsc) =>
                $"Creating proposal to move cards from {bsc.GetString()}...",
            "propose_bulk_move" => "Creating proposal to move cards...",
            "propose_create_column" when arguments.TryGetProperty("name", out var colName) =>
                $"Creating proposal for column \"{colName.GetString()}\"...",
            "propose_create_column" => "Creating proposal for new column...",
            _ => $"Executing {toolName}..."
        };
    }

    /// <summary>
    /// Builds a JSON metadata string from the tool call log for persistence on ChatMessage.
    /// </summary>
    public static string? BuildToolCallMetadataJson(IReadOnlyList<ToolCallLogEntry> log, int totalRounds, int totalTokens)
    {
        if (log.Count == 0) return null;

        var metadata = new
        {
            rounds = totalRounds,
            total_tokens = totalTokens,
            tool_calls = log.Select(e => new
            {
                round = e.Round,
                tool = e.ToolName,
                args = e.Arguments,
                result_summary = e.ResultSummary,
                is_error = e.IsError
            }).ToArray()
        };

        return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
    }

    private static ToolCallingResult BuildTimeoutResult(
        int tokensUsed, List<ToolCallLogEntry> log, string provider, string model)
    {
        var partialSummary = log.Count > 0
            ? " Here is what I found so far: " + string.Join("; ",
                log.Where(e => !e.IsError).Select(e => $"[{e.ToolName}]"))
            : "";

        return new ToolCallingResult(
            Content: $"I was unable to complete this request within the allowed time.{partialSummary}",
            TokensUsed: tokensUsed,
            Provider: provider,
            Model: model,
            Rounds: log.Count > 0 ? log.Max(e => e.Round) : 0,
            ToolCallLog: log,
            IsDegraded: true,
            DegradedReason: "Orchestration timeout exceeded.");
    }

    private static ToolCallingResult BuildLoopDetectedResult(
        int tokensUsed, List<ToolCallLogEntry> log, string provider, string model, int round)
    {
        var partialSummary = log.Count > 0
            ? " Here is what I found so far: " + string.Join("; ",
                log.Where(e => !e.IsError).Select(e => $"[{e.ToolName}]"))
            : "";

        return new ToolCallingResult(
            Content: $"I noticed I was repeating the same action and stopped to avoid an infinite loop.{partialSummary}",
            TokensUsed: tokensUsed,
            Provider: provider,
            Model: model,
            Rounds: round,
            ToolCallLog: log,
            IsDegraded: true,
            DegradedReason: "Tool-calling loop detected: identical tool calls in consecutive rounds.");
    }

    /// <summary>
    /// Computes a deterministic fingerprint of a set of tool calls (name + arguments)
    /// so that consecutive identical rounds can be detected.
    /// </summary>
    internal static string ComputeToolCallFingerprint(IReadOnlyList<ToolCallRequest> toolCalls)
    {
        // Sort by tool name for determinism when parallel calls arrive in varying order
        var sorted = toolCalls.OrderBy(tc => tc.ToolName, StringComparer.Ordinal)
            .ThenBy(tc => tc.Arguments.GetRawText(), StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var tc in sorted)
        {
            sb.Append(tc.ToolName);
            sb.Append(':');
            sb.Append(tc.Arguments.GetRawText());
            sb.Append('|');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static ToolCallingResult BuildExhaustedResult(
        int tokensUsed, List<ToolCallLogEntry> log, string provider, string model)
    {
        var partialSummary = log.Count > 0
            ? " Here is what I found: " + string.Join("; ",
                log.Where(e => !e.IsError).Select(e => $"[{e.ToolName}]"))
            : "";

        return new ToolCallingResult(
            Content: $"I was unable to complete this request within the allowed steps.{partialSummary}",
            TokensUsed: tokensUsed,
            Provider: provider,
            Model: model,
            Rounds: MaxRounds,
            ToolCallLog: log,
            IsDegraded: true,
            DegradedReason: "Maximum tool-calling rounds exceeded.");
    }

    private static ToolCallingResult BuildDegradedResult(
        string provider, string model,
        int tokensUsed = 0, int round = 0,
        List<ToolCallLogEntry>? log = null,
        string? reason = null)
    {
        return new ToolCallingResult(
            Content: null,
            TokensUsed: tokensUsed,
            Provider: provider,
            Model: model,
            Rounds: round,
            ToolCallLog: log ?? new List<ToolCallLogEntry>(),
            IsDegraded: true,
            DegradedReason: reason ?? "Tool calling is not available; falling back to single-turn.");
    }

    /// <summary>
    /// Truncates a tool result string to the configured byte budget so oversized
    /// payloads do not blow out the provider's context window.
    /// When <paramref name="maxBytes"/> is 0 or negative, no truncation is applied.
    /// A "(truncated)" marker is appended so the LLM knows the result was cut short.
    /// </summary>
    internal static string TruncateToolResult(string content, int maxBytes)
    {
        if (maxBytes <= 0 || string.IsNullOrEmpty(content))
            return content;

        var encoded = System.Text.Encoding.UTF8.GetByteCount(content);
        if (encoded <= maxBytes)
            return content;

        // Truncate by characters (approximate — UTF-8 multi-byte chars are uncommon
        // in typical JSON tool results but we avoid cutting in the middle of a char).
        const string marker = "...(truncated)";
        // Walk back until the byte count fits
        var maxChars = maxBytes - System.Text.Encoding.UTF8.GetByteCount(marker);
        if (maxChars <= 0) return marker;

        // Binary-search-like: estimate character count from byte ratio then clamp
        var ratio = (double)maxChars / encoded;
        var estimate = (int)(content.Length * ratio);
        while (estimate > 0 && System.Text.Encoding.UTF8.GetByteCount(content[..estimate]) > maxChars)
            estimate--;

        return estimate > 0 ? content[..estimate] + marker : marker;
    }

    private static string TruncateForLog(string content)
    {
        const int maxLength = 200;
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
        return content[..maxLength] + "...(truncated)";
    }
}

/// <summary>
/// The complete result of a tool-calling orchestration, including the final content,
/// token usage, and a log of all tool calls made during the conversation.
/// </summary>
public record ToolCallingResult(
    string? Content,
    int TokensUsed,
    string Provider,
    string Model,
    int Rounds,
    IReadOnlyList<ToolCallLogEntry> ToolCallLog,
    bool IsDegraded = false,
    string? DegradedReason = null
);

/// <summary>
/// A single tool call in the orchestration log, for auditing and metadata persistence.
/// </summary>
public record ToolCallLogEntry(
    int Round,
    string ToolName,
    JsonElement Arguments,
    string ResultSummary,
    bool IsError
);

/// <summary>
/// Interface for sending tool status events to the frontend via SignalR.
/// </summary>
public interface IToolStatusNotifier
{
    Task NotifyToolStatusAsync(
        Guid boardId,
        string toolName,
        string displayMessage,
        int round,
        int maxRounds,
        CancellationToken ct = default);
}
