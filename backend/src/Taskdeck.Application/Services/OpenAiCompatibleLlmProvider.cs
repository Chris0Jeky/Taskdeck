using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Taskdeck.Application.Services;

/// <summary>
/// OpenAI chat-completions-compatible provider for vendor gateways. This is kept
/// separate from <see cref="OpenAiLlmProvider"/> so api.openai.com defaults and
/// its existing non-streaming semantics remain unchanged.
/// </summary>
public sealed class OpenAiCompatibleLlmProvider : ILlmProvider
{
    private const string ProviderName = "OpenAICompatible";
    private const string BufferedStreamingFallbackReason =
        "Upstream endpoint rejected SSE streaming; the response was completed before emission.";

    private readonly HttpClient _httpClient;
    private readonly LlmProviderSettings _settings;
    private readonly ILogger<OpenAiCompatibleLlmProvider> _logger;

    public OpenAiCompatibleLlmProvider(
        HttpClient httpClient,
        LlmProviderSettings settings,
        ILogger<OpenAiCompatibleLlmProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
    {
        var userMessage = GetLastUserMessage(request);
        if (!LlmProviderSelectionPolicy.TryValidateOpenAiCompatibleSettings(_settings, out var validationError))
        {
            _logger.LogWarning("OpenAI-compatible provider configuration invalid: {Error}", validationError);
            return BuildFallbackResult(userMessage, "Live provider configuration is invalid.");
        }

        try
        {
            var useInstructionExtraction = request.SystemPrompt is null;
            var response = await SendCompletionAsync(request, stream: false, includeResponseFormat: useInstructionExtraction, ct);
            if (!response.IsSuccessStatusCode && useInstructionExtraction && IsResponseFormatRejection(response.StatusCode))
            {
                response.Dispose();
                _logger.LogInformation("OpenAI-compatible endpoint rejected response_format; retrying with prompt-enforced JSON only.");
                response = await SendCompletionAsync(request, stream: false, includeResponseFormat: false, ct);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OpenAI-compatible completion request failed with status code {StatusCode}.", (int)response.StatusCode);
                    return BuildFallbackResult(userMessage, "Live provider request failed.");
                }

                if (!TryParseResponse(body, out var content, out var tokensUsed, out var finishReason))
                {
                    _logger.LogWarning("OpenAI-compatible completion response could not be parsed.");
                    return BuildFallbackResult(userMessage, "Live provider response parsing failed.");
                }

                if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) ||
                    (useInstructionExtraction && OpenAiLlmProvider.LooksLikeTruncatedJson(content)))
                {
                    return new LlmCompletionResult(
                        content, tokensUsed, false, Provider: ProviderName, Model: GetConfiguredModelOrDefault(),
                        IsDegraded: true, DegradedReason: "Response was truncated");
                }

                if (useInstructionExtraction && LlmInstructionExtractionPrompt.TryParseStructuredResponse(
                        content, out var reply, out var actionable, out var instructions))
                {
                    return new LlmCompletionResult(
                        reply, tokensUsed, actionable, actionable ? "llm.extracted" : null,
                        ProviderName, GetConfiguredModelOrDefault(), Instructions: instructions.Count > 0 ? instructions : null);
                }

                var (isActionable, actionIntent) = LlmIntentClassifier.Classify(userMessage);
                var fallbackInstructions = isActionable
                    ? NaturalLanguageInstructionExtractor.Extract(userMessage, actionIntent)
                    : [];
                return new LlmCompletionResult(
                    content, tokensUsed, isActionable, actionIntent, ProviderName, GetConfiguredModelOrDefault(),
                    Instructions: fallbackInstructions.Count > 0 ? fallbackInstructions : null);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "OpenAI-compatible completion request failed with unexpected error. {ExceptionSummary}",
                SensitiveDataRedactor.SummarizeException(ex));
            return BuildFallbackResult(userMessage, "Live provider request errored.");
        }
    }

    public async IAsyncEnumerable<LlmTokenEvent> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!LlmProviderSelectionPolicy.TryValidateOpenAiCompatibleSettings(_settings, out var validationError))
        {
            yield return new LlmTokenEvent(string.Empty, true, Error: $"Live provider configuration is invalid: {validationError}",
                Provider: ProviderName, Model: GetConfiguredModelOrDefault());
            yield break;
        }

        HttpResponseMessage? response = null;
        try
        {
            // Streaming chat is conversational text, not the buffered instruction-extraction
            // contract. Suppress response_format and the JSON-only default system prompt so the
            // client receives the vendor's real text deltas rather than a JSON envelope.
            var streamingRequest = request.SystemPrompt is null ? request with { SystemPrompt = string.Empty } : request;
            using var message = CreateRequestMessage(streamingRequest, stream: true, includeResponseFormat: false);
            response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                response.Dispose();
                response = null;
                if (IsStreamingRejection(statusCode))
                {
                    await foreach (var fallback in EmitBufferedFallbackAsync(request, ct))
                        yield return fallback;
                    yield break;
                }

                yield return new LlmTokenEvent(string.Empty, true,
                    Error: $"OpenAI-compatible streaming request failed with status code {(int)statusCode}.",
                    Provider: ProviderName, Model: GetConfiguredModelOrDefault());
                yield break;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();
                response = null;
                if (TryParseResponse(body, out var content, out var tokensUsed, out _))
                {
                    yield return new LlmTokenEvent(content, true, TokensUsed: tokensUsed, Provider: ProviderName,
                        Model: GetConfiguredModelOrDefault(), IsDegraded: true,
                        DegradedReason: BufferedStreamingFallbackReason);
                }
                else
                {
                    yield return new LlmTokenEvent(string.Empty, true,
                        Error: "OpenAI-compatible endpoint returned a non-SSE response that could not be parsed.",
                        Provider: ProviderName, Model: GetConfiguredModelOrDefault());
                }
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var streamedContent = new StringBuilder();
            var data = new StringBuilder();
            var completed = false;

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                ct.ThrowIfCancellationRequested();
                if (line.Length == 0)
                {
                    if (data.Length == 0)
                        continue;

                    var eventData = data.ToString();
                    data.Clear();
                    var parsed = TryParseSseEvent(eventData);
                    if (parsed.Error is not null)
                    {
                        yield return new LlmTokenEvent(string.Empty, true, Error: parsed.Error,
                            Provider: ProviderName, Model: GetConfiguredModelOrDefault());
                        yield break;
                    }

                    if (!string.IsNullOrEmpty(parsed.Delta))
                    {
                        streamedContent.Append(parsed.Delta);
                        yield return new LlmTokenEvent(parsed.Delta, false, Provider: ProviderName, Model: GetConfiguredModelOrDefault());
                    }

                    if (parsed.IsDone)
                    {
                        completed = true;
                        yield return new LlmTokenEvent(string.Empty, true,
                            TokensUsed: parsed.TokensUsed ?? EstimateTokens(streamedContent.ToString()),
                            Provider: ProviderName, Model: GetConfiguredModelOrDefault());
                        yield break;
                    }

                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0)
                        data.Append('\n');
                    data.Append(line[5..].TrimStart());
                }
            }

            // A compliant SSE sender terminates each event with a blank line, but
            // process a final unterminated data field as well so a connection close
            // cannot turn an otherwise complete response into a false parse error.
            if (data.Length > 0)
            {
                var parsed = TryParseSseEvent(data.ToString());
                if (parsed.Error is not null)
                {
                    yield return new LlmTokenEvent(string.Empty, true, Error: parsed.Error,
                        Provider: ProviderName, Model: GetConfiguredModelOrDefault());
                    yield break;
                }

                if (!string.IsNullOrEmpty(parsed.Delta))
                {
                    streamedContent.Append(parsed.Delta);
                    yield return new LlmTokenEvent(parsed.Delta, false, Provider: ProviderName, Model: GetConfiguredModelOrDefault());
                }

                if (parsed.IsDone)
                {
                    completed = true;
                    yield return new LlmTokenEvent(string.Empty, true,
                        TokensUsed: parsed.TokensUsed ?? EstimateTokens(streamedContent.ToString()),
                        Provider: ProviderName, Model: GetConfiguredModelOrDefault());
                    yield break;
                }
            }

            if (!completed)
            {
                yield return new LlmTokenEvent(string.Empty, true,
                    Error: "OpenAI-compatible SSE stream ended before a completion marker.",
                    Provider: ProviderName, Model: GetConfiguredModelOrDefault());
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        var isValid = LlmProviderSelectionPolicy.TryValidateOpenAiCompatibleSettings(_settings, out var error);
        return Task.FromResult(new LlmHealthStatus(isValid, ProviderName, isValid ? null : error, GetConfiguredModelOrDefault()));
    }

    public async Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
    {
        var result = await CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("user", "Reply with exactly: OK")], MaxTokens: 4, Temperature: 0, SystemPrompt: string.Empty), ct);
        return new LlmHealthStatus(!result.IsDegraded, ProviderName,
            result.IsDegraded ? result.DegradedReason : null, GetConfiguredModelOrDefault(), IsProbed: true);
    }

    private async IAsyncEnumerable<LlmTokenEvent> EmitBufferedFallbackAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var result = await CompleteAsync(request, ct);
        yield return new LlmTokenEvent(result.Content, true, TokensUsed: result.TokensUsed, Provider: ProviderName,
            Model: result.Model, IsDegraded: true,
            DegradedReason: result.IsDegraded
                ? $"{BufferedStreamingFallbackReason} {result.DegradedReason}"
                : BufferedStreamingFallbackReason);
    }

    private async Task<HttpResponseMessage> SendCompletionAsync(
        ChatCompletionRequest request,
        bool stream,
        bool includeResponseFormat,
        CancellationToken ct)
    {
        using var message = CreateRequestMessage(request, stream, includeResponseFormat);
        return await _httpClient.SendAsync(message, ct);
    }

    private HttpRequestMessage CreateRequestMessage(ChatCompletionRequest request, bool stream, bool includeResponseFormat)
    {
        var compatible = _settings.OpenAiCompatible;
        var message = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsEndpoint());
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", compatible.ApiKey.Trim());
        LlmRequestAttributionMapper.AddAttributionHeaders(message, request.Attribution);
        foreach (var (name, value) in compatible.ExtraHeaders)
        {
            message.Headers.Add(name, value);
        }
        message.Content = JsonContent.Create(BuildRequestPayload(request, stream, includeResponseFormat));
        return message;
    }

    private object BuildRequestPayload(ChatCompletionRequest request, bool stream, bool includeResponseFormat)
    {
        var systemPrompt = LlmSystemPromptBuilder.BuildEffectiveSystemPrompt(
            request.SystemPrompt ?? LlmInstructionExtractionPrompt.SystemPrompt, request.BoardContext);
        var messages = new List<object>();
        if (!string.IsNullOrEmpty(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.AddRange(request.Messages.Select(MapMessage));

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _settings.OpenAiCompatible.Model.Trim(),
            ["messages"] = messages.ToArray(),
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["stream"] = stream
        };
        if (includeResponseFormat)
            payload["response_format"] = new { type = "json_object" };
        if (request.Attribution is not null)
            payload["user"] = LlmRequestAttributionMapper.BuildUserToken(request.Attribution.UserId);
        return payload;
    }

    private static SseEventParseResult TryParseSseEvent(string eventData)
    {
        if (string.Equals(eventData.Trim(), "[DONE]", StringComparison.Ordinal))
            return new SseEventParseResult(IsDone: true);

        try
        {
            using var document = JsonDocument.Parse(eventData);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out _))
                return new SseEventParseResult(Error: "OpenAI-compatible SSE stream reported an upstream error.");
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return new SseEventParseResult(Error: "OpenAI-compatible SSE event did not contain choices.");

            var first = choices[0];
            var delta = first.TryGetProperty("delta", out var deltaElement) &&
                        deltaElement.TryGetProperty("content", out var contentElement) &&
                        contentElement.ValueKind == JsonValueKind.String
                ? contentElement.GetString()
                : null;
            var finishReason = first.TryGetProperty("finish_reason", out var finish) && finish.ValueKind != JsonValueKind.Null
                ? finish.GetString()
                : null;
            var tokensUsed = root.TryGetProperty("usage", out var usage) &&
                             usage.TryGetProperty("total_tokens", out var total) && total.TryGetInt32(out var parsedTokens)
                ? (int?)parsedTokens
                : null;
            return new SseEventParseResult(delta, !string.IsNullOrEmpty(finishReason), tokensUsed);
        }
        catch (JsonException)
        {
            return new SseEventParseResult(Error: "OpenAI-compatible SSE stream contained malformed JSON.");
        }
    }

    private static bool TryParseResponse(string body, out string content, out int tokensUsed, out string? finishReason)
    {
        content = string.Empty;
        tokensUsed = 0;
        finishReason = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return false;
            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var contentElement))
                return false;
            content = contentElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
                return false;
            finishReason = choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String
                ? finish.GetString()
                : null;
            tokensUsed = root.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var total) && total.TryGetInt32(out var parsed)
                ? parsed
                : EstimateTokens(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsResponseFormatRejection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;

    private static bool IsStreamingRejection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or
            HttpStatusCode.NotImplemented or HttpStatusCode.UnprocessableEntity;

    private string BuildChatCompletionsEndpoint() => $"{_settings.OpenAiCompatible.BaseUrl.TrimEnd('/')}/chat/completions";

    private string GetConfiguredModelOrDefault() => string.IsNullOrWhiteSpace(_settings.OpenAiCompatible?.Model)
        ? "openai-compatible-unknown-model"
        : _settings.OpenAiCompatible.Model.Trim();

    private static object MapMessage(ChatCompletionMessage message) => new
    {
        role = (message.Role ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "assistant" => "assistant",
            "system" => "system",
            _ => "user"
        },
        content = message.Content
    };

    private static string GetLastUserMessage(ChatCompletionRequest request) => request.Messages
        .LastOrDefault(message => string.Equals(message.Role, "User", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;

    private static int EstimateTokens(string text) => Math.Max(1, string.IsNullOrWhiteSpace(text) ? 1 : text.Length / 4);

    private LlmCompletionResult BuildFallbackResult(string userMessage, string reason)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(userMessage);
        var instructions = isActionable ? NaturalLanguageInstructionExtractor.Extract(userMessage, actionIntent) : [];
        var content = isActionable
            ? $"I can help with that. I'll create a proposal to {actionIntent}. ({reason})"
            : $"I can help with that request. ({reason})";
        return new LlmCompletionResult(content, EstimateTokens(userMessage) + EstimateTokens(content), isActionable, actionIntent,
            ProviderName, GetConfiguredModelOrDefault(), true, reason, instructions.Count > 0 ? instructions : null);
    }

    private sealed record SseEventParseResult(string? Delta = null, bool IsDone = false, int? TokensUsed = null, string? Error = null);
}
