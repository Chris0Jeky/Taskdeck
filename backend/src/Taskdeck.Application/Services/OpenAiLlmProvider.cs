using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Taskdeck.Application.Services;

public class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly LlmProviderSettings _settings;
    private readonly ILogger<OpenAiLlmProvider> _logger;

    public OpenAiLlmProvider(
        HttpClient httpClient,
        LlmProviderSettings settings,
        ILogger<OpenAiLlmProvider> logger)
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

        if (!LlmProviderSelectionPolicy.TryValidateOpenAiSettings(_settings, out var validationError))
        {
            _logger.LogWarning("OpenAI provider configuration invalid: {Error}", validationError);
            return BuildFallbackResult(lastUserMessage, "Live provider configuration is invalid.", GetConfiguredModelOrDefault());
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsEndpoint());
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.OpenAi.ApiKey.Trim());
            LlmRequestAttributionMapper.AddAttributionHeaders(message, request.Attribution);
            message.Content = JsonContent.Create(BuildRequestPayload(request));

            using var response = await _httpClient.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenAI completion request failed with status code {StatusCode}.",
                    (int)response.StatusCode);
                return BuildFallbackResult(lastUserMessage, "Live provider request failed.", GetConfiguredModelOrDefault());
            }

            if (!TryParseResponse(body, out var content, out var tokensUsed, out var finishReason))
            {
                _logger.LogWarning("OpenAI completion response could not be parsed.");
                return BuildFallbackResult(lastUserMessage, "Live provider response parsing failed.", GetConfiguredModelOrDefault());
            }

            // Detect truncation: OpenAI returns finish_reason "length" when the
            // response was cut off by the max_tokens limit.
            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("OpenAI response was truncated (finish_reason=length).");
                return new LlmCompletionResult(
                    content,
                    tokensUsed,
                    IsActionable: false,
                    Provider: "OpenAI",
                    Model: GetConfiguredModelOrDefault(),
                    IsDegraded: true,
                    DegradedReason: "Response was truncated");
            }

            // When JSON mode was requested and the response starts with '{' but
            // does not parse as valid JSON, the output was likely truncated.
            var useInstructionExtraction = request.SystemPrompt is null;
            if (useInstructionExtraction && LooksLikeTruncatedJson(content))
            {
                _logger.LogWarning("OpenAI JSON-mode response is not valid JSON; treating as truncated.");
                return new LlmCompletionResult(
                    content,
                    tokensUsed,
                    IsActionable: false,
                    Provider: "OpenAI",
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
                    "OpenAI",
                    GetConfiguredModelOrDefault(),
                    Instructions: structuredInstructions.Count > 0 ? structuredInstructions : null);
            }

            // Fallback to static classifier when structured parse fails
            _logger.LogDebug("OpenAI response was not structured JSON; falling back to static classifier.");
            var (isActionable, actionIntent) = LlmIntentClassifier.Classify(lastUserMessage);
            List<string>? fallbackInstructions = null;
            if (isActionable)
            {
                var extracted = NaturalLanguageInstructionExtractor.Extract(lastUserMessage, actionIntent);
                if (extracted.Count > 0)
                    fallbackInstructions = extracted;
            }
            return new LlmCompletionResult(content, tokensUsed, isActionable, actionIntent, "OpenAI", GetConfiguredModelOrDefault(), Instructions: fallbackInstructions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "OpenAI completion request failed with unexpected error. {ExceptionSummary}",
                SensitiveDataRedactor.SummarizeException(ex));
            return BuildFallbackResult(lastUserMessage, "Live provider request errored.", GetConfiguredModelOrDefault());
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
        if (!LlmProviderSelectionPolicy.TryValidateOpenAiSettings(_settings, out var error))
        {
            return Task.FromResult(new LlmHealthStatus(false, "OpenAI", error, GetConfiguredModelOrDefault()));
        }

        return Task.FromResult(new LlmHealthStatus(true, "OpenAI", Model: GetConfiguredModelOrDefault()));
    }

    public async Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
    {
        var model = GetConfiguredModelOrDefault();

        if (!LlmProviderSelectionPolicy.TryValidateOpenAiSettings(_settings, out var validationError))
        {
            return new LlmHealthStatus(false, "OpenAI", validationError, model, IsProbed: true);
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
                return new LlmHealthStatus(false, "OpenAI", result.DegradedReason ?? "Probe returned degraded response.", model, IsProbed: true);
            }

            return new LlmHealthStatus(true, "OpenAI", Model: model, IsProbed: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI probe failed: {Message}", ex.Message);
            return new LlmHealthStatus(false, "OpenAI", $"Probe failed: {ex.Message}", model, IsProbed: true);
        }
    }

    private string BuildChatCompletionsEndpoint()
    {
        var baseUrl = (_settings.OpenAi?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        return $"{baseUrl}/chat/completions";
    }

    private string GetConfiguredModelOrDefault()
    {
        return string.IsNullOrWhiteSpace(_settings.OpenAi?.Model)
            ? "openai-unknown-model"
            : _settings.OpenAi.Model.Trim();
    }

    private static object MapMessage(ChatCompletionMessage message)
    {
        var normalizedRole = (message.Role ?? string.Empty).Trim().ToLowerInvariant();
        return new
        {
            role = normalizedRole switch
            {
                "assistant" => "assistant",
                "system" => "system",
                _ => "user"
            },
            content = message.Content
        };
    }

    private object BuildRequestPayload(ChatCompletionRequest request)
    {
        var messages = new List<object>();

        // Only inject system prompt and JSON mode when no explicit SystemPrompt override
        // is provided. Probe requests and other special calls pass SystemPrompt = ""
        // to opt out of instruction extraction.
        var useInstructionExtraction = request.SystemPrompt is null;
        var baseSystemPrompt = request.SystemPrompt ?? LlmInstructionExtractionPrompt.SystemPrompt;

        // Append board context when available so the LLM knows the board's structure
        var systemPrompt = LlmSystemPromptBuilder.BuildEffectiveSystemPrompt(baseSystemPrompt, request.BoardContext);

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        messages.AddRange(request.Messages.Select(MapMessage));

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _settings.OpenAi.Model.Trim(),
            ["messages"] = messages.ToArray(),
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["stream"] = false
        };

        if (useInstructionExtraction)
        {
            payload["response_format"] = new { type = "json_object" };
        }

        if (request.Attribution is not null)
        {
            payload["user"] = LlmRequestAttributionMapper.BuildUserToken(request.Attribution.UserId);
        }

        return payload;
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

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return false;
            }

            var first = choices[0];
            if (!first.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var contentElement))
            {
                return false;
            }

            content = contentElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            if (first.TryGetProperty("finish_reason", out var finishReasonElement) &&
                finishReasonElement.ValueKind == JsonValueKind.String)
            {
                finishReason = finishReasonElement.GetString();
            }

            if (root.TryGetProperty("usage", out var usage) &&
                usage.TryGetProperty("total_tokens", out var totalTokens) &&
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
            Provider: "OpenAI",
            Model: string.IsNullOrWhiteSpace(model) ? "openai-unknown-model" : model.Trim(),
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
