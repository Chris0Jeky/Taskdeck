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

            if (!TryParseResponse(body, out var content, out var tokensUsed))
            {
                _logger.LogWarning("OpenAI completion response could not be parsed.");
                return BuildFallbackResult(lastUserMessage, "Live provider response parsing failed.", GetConfiguredModelOrDefault());
            }

            var (isActionable, actionIntent) = LlmIntentClassifier.Classify(lastUserMessage);
            return new LlmCompletionResult(content, tokensUsed, isActionable, actionIntent, "OpenAI", GetConfiguredModelOrDefault());
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
            var probeRequest = new ChatCompletionRequest(
                [new ChatCompletionMessage("user", "Reply with exactly: OK")],
                MaxTokens: 4,
                Temperature: 0);

            var result = await CompleteAsync(probeRequest, ct);

            if (result.IsDegraded)
            {
                return new LlmHealthStatus(false, "OpenAI", result.DegradedReason ?? "Probe returned degraded response.", model, IsProbed: true);
            }

            return new LlmHealthStatus(true, "OpenAI", Model: model, IsProbed: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("OpenAI probe failed: {Message}", ex.Message);
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
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _settings.OpenAi.Model.Trim(),
            ["messages"] = request.Messages.Select(MapMessage).ToArray(),
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["stream"] = false
        };

        if (request.Attribution is not null)
        {
            payload["user"] = LlmRequestAttributionMapper.BuildUserToken(request.Attribution.UserId);
        }

        return payload;
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
