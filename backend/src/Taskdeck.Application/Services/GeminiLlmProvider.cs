using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Taskdeck.Application.Services;

public class GeminiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly LlmProviderSettings _settings;
    private readonly ILogger<GeminiLlmProvider> _logger;

    public GeminiLlmProvider(
        HttpClient httpClient,
        LlmProviderSettings settings,
        ILogger<GeminiLlmProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
    {
        var lastUserMessage = request.Messages
            .LastOrDefault(m => string.Equals(m.Role, "User", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? string.Empty;

        if (!LlmProviderSelectionPolicy.TryValidateGeminiSettings(_settings, out var validationError))
        {
            _logger.LogWarning("Gemini provider configuration invalid: {Error}", validationError);
            return BuildFallbackResult(lastUserMessage, "Live provider configuration is invalid.", GetConfiguredModelOrDefault());
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, BuildGenerateContentEndpoint());
            message.Headers.TryAddWithoutValidation("x-goog-api-key", (_settings.Gemini?.ApiKey ?? string.Empty).Trim());
            LlmRequestAttributionMapper.AddAttributionHeaders(message, request.Attribution);
            var useInstructionExtraction = request.SystemPrompt is null;
            var baseSystemPrompt = request.SystemPrompt ?? LlmInstructionExtractionPrompt.SystemPrompt;

            // Append board context when available so the LLM knows the board's structure
            var systemPrompt = LlmSystemPromptBuilder.BuildEffectiveSystemPrompt(baseSystemPrompt, request.BoardContext);

            var generationConfig = useInstructionExtraction
                ? (object)new
                {
                    temperature = request.Temperature,
                    maxOutputTokens = request.MaxTokens,
                    responseMimeType = "application/json"
                }
                : new
                {
                    temperature = request.Temperature,
                    maxOutputTokens = request.MaxTokens
                };

            message.Content = !string.IsNullOrEmpty(systemPrompt)
                ? JsonContent.Create(new
                {
                    contents = request.Messages.Select(MapMessage).ToArray(),
                    generationConfig,
                    system_instruction = new { parts = new[] { new { text = systemPrompt } } }
                })
                : JsonContent.Create(new
                {
                    contents = request.Messages.Select(MapMessage).ToArray(),
                    generationConfig
                });

            using var response = await _httpClient.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Gemini completion request failed with status code {StatusCode}.",
                    (int)response.StatusCode);
                return BuildFallbackResult(lastUserMessage, "Live provider request failed.", GetConfiguredModelOrDefault());
            }

            if (!TryParseResponse(body, out var content, out var tokensUsed, out var finishReason))
            {
                _logger.LogWarning("Gemini completion response could not be parsed.");
                return BuildFallbackResult(lastUserMessage, "Live provider response parsing failed.", GetConfiguredModelOrDefault());
            }

            // Detect truncation: Gemini returns finishReason "MAX_TOKENS" when the
            // response was cut off by the maxOutputTokens limit.
            if (string.Equals(finishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Gemini response was truncated (finishReason=MAX_TOKENS).");
                return new LlmCompletionResult(
                    content,
                    tokensUsed,
                    IsActionable: false,
                    Provider: "Gemini",
                    Model: GetConfiguredModelOrDefault(),
                    IsDegraded: true,
                    DegradedReason: "Response was truncated");
            }

            // When JSON mode was requested and the response starts with '{' but
            // does not parse as valid JSON, the output was likely truncated.
            if (useInstructionExtraction && LooksLikeTruncatedJson(content))
            {
                _logger.LogWarning("Gemini JSON-mode response is not valid JSON; treating as truncated.");
                return new LlmCompletionResult(
                    content,
                    tokensUsed,
                    IsActionable: false,
                    Provider: "Gemini",
                    Model: GetConfiguredModelOrDefault(),
                    IsDegraded: true,
                    DegradedReason: "Response was truncated");
            }

            // Try to parse structured instruction extraction from the LLM response
            if (LlmInstructionExtractionPrompt.TryParseStructuredResponse(
                    content,
                    out var structuredReply,
                    out var structuredActionable,
                    out var structuredInstructions))
            {
                return new LlmCompletionResult(
                    structuredReply,
                    tokensUsed,
                    structuredActionable,
                    structuredActionable ? "llm.extracted" : null,
                    "Gemini",
                    GetConfiguredModelOrDefault(),
                    Instructions: structuredInstructions.Count > 0 ? structuredInstructions : null);
            }

            // Fallback to static classifier when structured parse fails
            _logger.LogDebug("Gemini response was not structured JSON; falling back to static classifier.");
            var (isActionable, actionIntent) = LlmIntentClassifier.Classify(lastUserMessage);
            List<string>? fallbackInstructions = null;
            if (isActionable)
            {
                var extracted = NaturalLanguageInstructionExtractor.Extract(lastUserMessage, actionIntent);
                if (extracted.Count > 0)
                    fallbackInstructions = extracted;
            }
            return new LlmCompletionResult(content, tokensUsed, isActionable, actionIntent, "Gemini", GetConfiguredModelOrDefault(), Instructions: fallbackInstructions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Gemini completion request failed with unexpected error. {ExceptionSummary}",
                SensitiveDataRedactor.SummarizeException(ex));
            return BuildFallbackResult(lastUserMessage, "Live provider request errored.", GetConfiguredModelOrDefault());
        }
    }

    public async Task<LlmToolCompletionResult> CompleteWithToolsAsync(
        ChatCompletionRequest request,
        IReadOnlyList<TaskdeckToolSchema> tools,
        IReadOnlyList<ToolCallResult>? previousToolResults = null,
        CancellationToken ct = default)
    {
        if (!LlmProviderSelectionPolicy.TryValidateGeminiSettings(_settings, out var validationError))
        {
            _logger.LogWarning("Gemini provider configuration invalid for tool call: {Error}", validationError);
            return new LlmToolCompletionResult(
                Content: "I'm unable to process tool calls right now due to a configuration issue.",
                TokensUsed: 0, Provider: "Gemini", Model: GetConfiguredModelOrDefault(),
                ToolCalls: null, IsComplete: true, IsDegraded: true,
                DegradedReason: "Live provider configuration is invalid.");
        }

        try
        {
            using var httpMessage = new HttpRequestMessage(HttpMethod.Post, BuildGenerateContentEndpoint());
            httpMessage.Headers.TryAddWithoutValidation("x-goog-api-key", (_settings.Gemini?.ApiKey ?? string.Empty).Trim());
            LlmRequestAttributionMapper.AddAttributionHeaders(httpMessage, request.Attribution);
            httpMessage.Content = JsonContent.Create(BuildToolCallingPayload(request, tools, previousToolResults));

            using var response = await _httpClient.SendAsync(httpMessage, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini tool-calling request failed with status code {StatusCode}.", (int)response.StatusCode);
                return new LlmToolCompletionResult(
                    Content: "I encountered an error while processing your request.",
                    TokensUsed: 0, Provider: "Gemini", Model: GetConfiguredModelOrDefault(),
                    ToolCalls: null, IsComplete: true, IsDegraded: true,
                    DegradedReason: "Live provider request failed.");
            }

            return ParseToolCallingResponse(body);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Gemini tool-calling request failed. {ExceptionSummary}",
                SensitiveDataRedactor.SummarizeException(ex));
            return new LlmToolCompletionResult(
                Content: "I encountered an unexpected error while processing your request.",
                TokensUsed: 0, Provider: "Gemini", Model: GetConfiguredModelOrDefault(),
                ToolCalls: null, IsComplete: true, IsDegraded: true,
                DegradedReason: "Live provider request errored.");
        }
    }

    internal object BuildToolCallingPayload(
        ChatCompletionRequest request,
        IReadOnlyList<TaskdeckToolSchema> tools,
        IReadOnlyList<ToolCallResult>? previousToolResults)
    {
        var contents = new List<object>();

        // Add conversation messages
        contents.AddRange(request.Messages.Select(MapMessage));

        // Add previous tool results: Gemini requires a model message with
        // functionCall parts followed by a user message with functionResponse parts.
        if (previousToolResults is { Count: > 0 })
        {
            // Synthetic model message with the functionCall that produced these results.
            // ToolCallResult now carries the original arguments so we replay them faithfully.
            var callParts = previousToolResults.Select(r => (object)new
            {
                functionCall = new
                {
                    name = r.ToolName,
                    args = r.Arguments.ValueKind != JsonValueKind.Undefined
                        ? (object)r.Arguments
                        : new { }
                }
            }).ToArray();
            contents.Add(new { role = "model", parts = callParts });

            // User message with functionResponse parts
            var responseParts = previousToolResults.Select(r =>
            {
                // Parse the tool result content as JSON, falling back to wrapping as text
                object responsePayload;
                try
                {
                    using var doc = JsonDocument.Parse(r.Content);
                    responsePayload = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    responsePayload = new { result = r.Content };
                }

                return (object)new
                {
                    functionResponse = new
                    {
                        name = r.ToolName,
                        response = responsePayload
                    }
                };
            }).ToArray();

            contents.Add(new { role = "user", parts = responseParts });
        }

        // Convert tool schemas to Gemini functionDeclarations format
        var functionDeclarations = tools.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            parameters = t.ParametersSchema
        }).ToArray();

        var systemPrompt = request.SystemPrompt ?? ToolCallingSystemPrompt.Prompt;

        var payload = new Dictionary<string, object?>
        {
            ["contents"] = contents.ToArray(),
            ["tools"] = new[] { new { functionDeclarations } },
            ["toolConfig"] = new { functionCallingConfig = new { mode = "AUTO" } },
            ["generationConfig"] = new
            {
                temperature = request.Temperature,
                maxOutputTokens = request.MaxTokens
            }
        };

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            payload["system_instruction"] = new { parts = new[] { new { text = systemPrompt } } };
        }

        return payload;
    }

    internal LlmToolCompletionResult ParseToolCallingResponse(string responseBody)
    {
        var model = GetConfiguredModelOrDefault();

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return new LlmToolCompletionResult(
                Content: "Received empty response from provider.",
                TokensUsed: 0, Provider: "Gemini", Model: model,
                ToolCalls: null, IsComplete: true, IsDegraded: true,
                DegradedReason: "Empty response.");
        }

        try
        {
            using var json = JsonDocument.Parse(responseBody);
            var root = json.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            {
                return new LlmToolCompletionResult(
                    Content: "Could not parse provider response.",
                    TokensUsed: 0, Provider: "Gemini", Model: model,
                    ToolCalls: null, IsComplete: true, IsDegraded: true,
                    DegradedReason: "No candidates in response.");
            }

            var firstCandidate = candidates[0];
            var tokensUsed = 0;
            if (root.TryGetProperty("usageMetadata", out var usage) &&
                usage.TryGetProperty("totalTokenCount", out var totalTokens) &&
                totalTokens.TryGetInt32(out var parsedTokens))
            {
                tokensUsed = parsedTokens;
            }

            if (!firstCandidate.TryGetProperty("content", out var candidateContent) ||
                !candidateContent.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
            {
                return new LlmToolCompletionResult(
                    Content: "Could not parse provider response parts.",
                    TokensUsed: tokensUsed, Provider: "Gemini", Model: model,
                    ToolCalls: null, IsComplete: true, IsDegraded: true,
                    DegradedReason: "No parts in response.");
            }

            // Check for functionCall parts
            var toolCalls = new List<ToolCallRequest>();
            var textParts = new List<string>();

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("functionCall", out var functionCall))
                {
                    var name = functionCall.TryGetProperty("name", out var nameEl)
                        ? nameEl.GetString() ?? "" : "";
                    var callId = functionCall.TryGetProperty("id", out var idEl)
                        ? idEl.GetString() ?? $"gemini-{Guid.NewGuid():N}"[..16]
                        : $"gemini-{Guid.NewGuid():N}"[..16];
                    JsonElement args;
                    if (functionCall.TryGetProperty("args", out var argsEl))
                    {
                        args = argsEl.Clone();
                    }
                    else
                    {
                        using var emptyDoc = JsonDocument.Parse("{}");
                        args = emptyDoc.RootElement.Clone();
                    }

                    toolCalls.Add(new ToolCallRequest(callId, name, args));
                }
                else if (part.TryGetProperty("text", out var textEl))
                {
                    var text = textEl.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        textParts.Add(text);
                }
            }

            if (toolCalls.Count > 0)
            {
                return new LlmToolCompletionResult(
                    Content: null,
                    TokensUsed: tokensUsed,
                    Provider: "Gemini",
                    Model: model,
                    ToolCalls: toolCalls,
                    IsComplete: false);
            }

            // No tool calls — final text response
            var content = string.Join(Environment.NewLine, textParts).Trim();
            return new LlmToolCompletionResult(
                Content: content.Length > 0 ? content : "I processed your request.",
                TokensUsed: tokensUsed,
                Provider: "Gemini",
                Model: model,
                ToolCalls: null,
                IsComplete: true);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to parse Gemini tool-calling response: {Message}", ex.Message);
            return new LlmToolCompletionResult(
                Content: "Could not parse provider response.",
                TokensUsed: 0, Provider: "Gemini", Model: model,
                ToolCalls: null, IsComplete: true, IsDegraded: true,
                DegradedReason: "Response parsing failed.");
        }
    }

    public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await CompleteAsync(request, ct);
        var tokens = result.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < tokens.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var token = (i == 0 ? string.Empty : " ") + tokens[i];
            yield return new LlmTokenEvent(token, i == tokens.Length - 1);
        }
    }

    public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        if (!LlmProviderSelectionPolicy.TryValidateGeminiSettings(_settings, out var error))
        {
            return Task.FromResult(new LlmHealthStatus(false, "Gemini", error, GetConfiguredModelOrDefault()));
        }

        return Task.FromResult(new LlmHealthStatus(true, "Gemini", Model: GetConfiguredModelOrDefault()));
    }

    public async Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
    {
        var model = GetConfiguredModelOrDefault();

        if (!LlmProviderSelectionPolicy.TryValidateGeminiSettings(_settings, out var validationError))
        {
            return new LlmHealthStatus(false, "Gemini", validationError, model, IsProbed: true);
        }

        try
        {
            // Pass empty SystemPrompt to opt out of instruction extraction / JSON mode for probes
            var probeRequest = new ChatCompletionRequest(
                [new ChatCompletionMessage("user", "Reply with exactly: OK")],
                MaxTokens: 4,
                Temperature: 0,
                SystemPrompt: string.Empty);

            var result = await CompleteAsync(probeRequest, ct);

            if (result.IsDegraded)
            {
                return new LlmHealthStatus(false, "Gemini", result.DegradedReason ?? "Probe returned degraded response.", model, IsProbed: true);
            }

            return new LlmHealthStatus(true, "Gemini", Model: model, IsProbed: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini probe failed: {Message}", ex.Message);
            return new LlmHealthStatus(false, "Gemini", $"Probe failed: {ex.Message}", model, IsProbed: true);
        }
    }

    private string BuildGenerateContentEndpoint()
    {
        var baseUrl = (_settings.Gemini?.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');
        var model = GetConfiguredModelOrDefault();
        if (!model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            model = $"models/{model}";
        }

        return $"{baseUrl}/{model}:generateContent";
    }

    private string GetConfiguredModelOrDefault()
    {
        return string.IsNullOrWhiteSpace(_settings.Gemini?.Model)
            ? "gemini-unknown-model"
            : _settings.Gemini.Model.Trim();
    }

    private static object MapMessage(ChatCompletionMessage message)
    {
        var normalizedRole = (message.Role ?? string.Empty).Trim().ToLowerInvariant();
        var geminiRole = normalizedRole switch
        {
            "assistant" => "model",
            "system" => "user",
            _ => "user"
        };

        return new
        {
            role = geminiRole,
            parts = new[]
            {
                new { text = message.Content }
            }
        };
    }

    private static bool TryParseResponse(string responseBody, out string content, out int tokensUsed, out string? finishReason)
    {
        content = string.Empty;
        tokensUsed = 0;
        finishReason = null;

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var json = JsonDocument.Parse(responseBody);
            var root = json.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return false;
            }

            var firstCandidate = candidates[0];

            if (firstCandidate.TryGetProperty("finishReason", out var finishReasonElement) &&
                finishReasonElement.ValueKind == JsonValueKind.String)
            {
                finishReason = finishReasonElement.GetString();
            }

            if (!firstCandidate.TryGetProperty("content", out var candidateContent) ||
                !candidateContent.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array ||
                parts.GetArrayLength() == 0)
            {
                return false;
            }

            var textParts = new List<string>();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textParts.Add(text);
                    }
                }
            }

            content = string.Join(Environment.NewLine, textParts).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            if (root.TryGetProperty("usageMetadata", out var usage) &&
                usage.TryGetProperty("totalTokenCount", out var totalTokens) &&
                totalTokens.TryGetInt32(out var parsedTokens))
            {
                tokensUsed = parsedTokens;
            }
            else
            {
                tokensUsed = EstimateTokens(content);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static LlmCompletionResult BuildFallbackResult(string userMessage, string reason, string model)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(userMessage);

        // When falling back to the static classifier, also extract structured
        // instructions so the parser can handle natural language input.
        List<string>? instructions = null;
        if (isActionable)
        {
            var extracted = NaturalLanguageInstructionExtractor.Extract(userMessage, actionIntent);
            if (extracted.Count > 0)
                instructions = extracted;
        }

        var content = isActionable
            ? $"I can help with that. I'll create a proposal to {actionIntent}. ({reason})"
            : $"I can help with that request. ({reason})";

        return new LlmCompletionResult(
            content,
            TokensUsed: EstimateTokens(userMessage) + EstimateTokens(content),
            IsActionable: isActionable,
            ActionIntent: actionIntent,
            Provider: "Gemini",
            Model: string.IsNullOrWhiteSpace(model) ? "gemini-unknown-model" : model.Trim(),
            IsDegraded: true,
            DegradedReason: reason,
            Instructions: instructions);
    }

    /// <summary>
    /// Returns true when <paramref name="text"/> starts with '{' but does not
    /// parse as valid JSON — a strong signal the response was cut off mid-output.
    /// </summary>
    internal static bool LooksLikeTruncatedJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 1;
        }

        return Math.Max(1, text.Length / 4);
    }
}
