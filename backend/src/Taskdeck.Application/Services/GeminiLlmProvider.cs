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
            var systemPrompt = request.SystemPrompt ?? LlmInstructionExtractionPrompt.SystemPrompt;

            // Append board context when available so the LLM knows the board's structure
            if (!string.IsNullOrEmpty(request.BoardContext))
            {
                systemPrompt = string.IsNullOrEmpty(systemPrompt)
                    ? request.BoardContext
                    : $"{systemPrompt}\n\n{request.BoardContext}";
            }

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

            if (!TryParseResponse(body, out var content, out var tokensUsed))
            {
                _logger.LogWarning("Gemini completion response could not be parsed.");
                return BuildFallbackResult(lastUserMessage, "Live provider response parsing failed.", GetConfiguredModelOrDefault());
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
            return new LlmCompletionResult(content, tokensUsed, isActionable, actionIntent, "Gemini", GetConfiguredModelOrDefault());
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

    private static bool TryParseResponse(string responseBody, out string content, out int tokensUsed)
    {
        content = string.Empty;
        tokensUsed = 0;

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
            DegradedReason: reason);
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
