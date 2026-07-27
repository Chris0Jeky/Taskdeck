using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Taskdeck.Application.Services;

public class OllamaLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly LlmProviderSettings _settings;
    private readonly ILogger<OllamaLlmProvider> _logger;

    public OllamaLlmProvider(
        HttpClient httpClient,
        LlmProviderSettings settings,
        ILogger<OllamaLlmProvider> logger)
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

        if (!LlmProviderSelectionPolicy.TryValidateOllamaSettings(_settings, out var validationError, allowLocalhostEndpoints: _settings.Ollama?.AllowLocalhostEndpoints ?? false))
        {
            _logger.LogWarning("Ollama provider configuration invalid: {Error}", validationError);
            return BuildFallbackResult(lastUserMessage, "Local provider configuration is invalid.", GetConfiguredModelOrDefault());
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, BuildChatEndpoint());
            LlmRequestAttributionMapper.AddAttributionHeaders(message, request.Attribution);
            message.Content = JsonContent.Create(BuildRequestPayload(request));

            using var response = await _httpClient.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama completion request failed with status code {StatusCode}.",
                    (int)response.StatusCode);
                return BuildFallbackResult(lastUserMessage, "Local provider request failed.", GetConfiguredModelOrDefault());
            }

            if (!TryParseResponse(body, out var content, out var tokensUsed, out var doneReason))
            {
                _logger.LogWarning("Ollama completion response could not be parsed.");
                return BuildFallbackResult(lastUserMessage, "Local provider response parsing failed.", GetConfiguredModelOrDefault());
            }

            if (string.Equals(doneReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Ollama response was truncated (done_reason=length).");
                return new LlmCompletionResult(
                    content,
                    tokensUsed,
                    IsActionable: false,
                    Provider: "Ollama",
                    Model: GetConfiguredModelOrDefault(),
                    IsDegraded: true,
                    DegradedReason: "Response was truncated");
            }

            var useInstructionExtraction = request.SystemPrompt is null;
            if (useInstructionExtraction && LooksLikeTruncatedJson(content))
            {
                _logger.LogWarning("Ollama JSON-mode response is not valid JSON; treating as truncated.");
                return new LlmCompletionResult(
                    content,
                    tokensUsed,
                    IsActionable: false,
                    Provider: "Ollama",
                    Model: GetConfiguredModelOrDefault(),
                    IsDegraded: true,
                    DegradedReason: "Response was truncated");
            }

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
                    "Ollama",
                    GetConfiguredModelOrDefault(),
                    Instructions: structuredInstructions.Count > 0 ? structuredInstructions : null);
            }

            _logger.LogDebug("Ollama response was not structured JSON; falling back to static classifier.");
            var (isActionable, actionIntent) = LlmIntentClassifier.Classify(lastUserMessage);
            List<string>? fallbackInstructions = null;
            if (isActionable)
            {
                var extracted = NaturalLanguageInstructionExtractor.Extract(lastUserMessage, actionIntent);
                if (extracted.Count > 0)
                    fallbackInstructions = extracted;
            }
            return new LlmCompletionResult(content, tokensUsed, isActionable, actionIntent, "Ollama", GetConfiguredModelOrDefault(), Instructions: fallbackInstructions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Ollama completion request failed with unexpected error. {ExceptionSummary}",
                SensitiveDataRedactor.SummarizeException(ex));
            return BuildFallbackResult(lastUserMessage, "Local provider request errored.", GetConfiguredModelOrDefault());
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
            var isLast = i == tokens.Length - 1;
            yield return isLast
                ? new LlmTokenEvent(token, true, TokensUsed: result.TokensUsed, Provider: result.Provider, Model: result.Model)
                {
                    IsDegraded = result.IsDegraded,
                    DegradedReason = result.DegradedReason
                }
                : new LlmTokenEvent(token, false);
        }
    }

    public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        if (!LlmProviderSelectionPolicy.TryValidateOllamaSettings(_settings, out var error, allowLocalhostEndpoints: _settings.Ollama?.AllowLocalhostEndpoints ?? false))
        {
            return Task.FromResult(new LlmHealthStatus(false, "Ollama", error, GetConfiguredModelOrDefault()));
        }

        return Task.FromResult(new LlmHealthStatus(true, "Ollama", Model: GetConfiguredModelOrDefault()));
    }

    public async Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
    {
        var model = GetConfiguredModelOrDefault();

        if (!LlmProviderSelectionPolicy.TryValidateOllamaSettings(_settings, out var validationError, allowLocalhostEndpoints: _settings.Ollama?.AllowLocalhostEndpoints ?? false))
        {
            return new LlmHealthStatus(false, "Ollama", validationError, model, IsProbed: true);
        }

        try
        {
            var probeRequest = new ChatCompletionRequest(
                [new ChatCompletionMessage("user", "Reply with exactly: OK")],
                MaxTokens: 4,
                Temperature: 0,
                SystemPrompt: string.Empty);

            var result = await CompleteAsync(probeRequest, ct);

            if (result.IsDegraded)
            {
                return new LlmHealthStatus(false, "Ollama", result.DegradedReason ?? "Probe returned degraded response.", model, IsProbed: true);
            }

            return new LlmHealthStatus(true, "Ollama", Model: model, IsProbed: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama probe failed: {Message}", ex.Message);
            return new LlmHealthStatus(false, "Ollama", $"Probe failed: {ex.Message}", model, IsProbed: true);
        }
    }

    private string BuildChatEndpoint()
    {
        var baseUrl = (_settings.Ollama?.BaseUrl ?? "http://localhost:11434").TrimEnd('/');
        return $"{baseUrl}/api/chat";
    }

    private string GetConfiguredModelOrDefault()
    {
        return string.IsNullOrWhiteSpace(_settings.Ollama?.Model)
            ? "ollama-unknown-model"
            : _settings.Ollama.Model.Trim();
    }

    private object BuildRequestPayload(ChatCompletionRequest request)
    {
        var messages = new List<object>();

        var useInstructionExtraction = request.SystemPrompt is null;
        var baseSystemPrompt = request.SystemPrompt ?? LlmInstructionExtractionPrompt.SystemPrompt;
        var systemPrompt = LlmSystemPromptBuilder.BuildEffectiveSystemPrompt(baseSystemPrompt, request.BoardContext);

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        messages.AddRange(request.Messages.Select(MapMessage));

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _settings.Ollama!.Model.Trim(),
            ["messages"] = messages.ToArray(),
            ["stream"] = false,
            ["options"] = new
            {
                temperature = request.Temperature,
                num_predict = request.MaxTokens
            }
        };

        if (useInstructionExtraction)
        {
            payload["format"] = "json";
        }

        return payload;
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

    internal static bool TryParseResponse(string responseBody, out string content, out int tokensUsed, out string? doneReason)
    {
        content = string.Empty;
        tokensUsed = 0;
        doneReason = null;

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var json = JsonDocument.Parse(responseBody);
            var root = json.RootElement;

            if (!root.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var contentElement))
            {
                return false;
            }

            content = contentElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            if (root.TryGetProperty("done_reason", out var doneReasonElement) &&
                doneReasonElement.ValueKind == JsonValueKind.String)
            {
                doneReason = doneReasonElement.GetString();
            }

            var parsedEval = 0;
            var parsedPromptEval = 0;
            var hasEvalCount = root.TryGetProperty("eval_count", out var evalCount) &&
                evalCount.TryGetInt32(out parsedEval);
            var hasPromptEvalCount = root.TryGetProperty("prompt_eval_count", out var promptEvalCount) &&
                promptEvalCount.TryGetInt32(out parsedPromptEval);

            if (hasEvalCount && hasPromptEvalCount)
            {
                tokensUsed = parsedEval + parsedPromptEval;
            }
            else if (hasEvalCount)
            {
                tokensUsed = parsedEval;
            }
            else if (hasPromptEvalCount)
            {
                tokensUsed = parsedPromptEval;
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
            Provider: "Ollama",
            Model: string.IsNullOrWhiteSpace(model) ? "ollama-unknown-model" : model.Trim(),
            IsDegraded: true,
            DegradedReason: reason,
            Instructions: instructions);
    }

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
