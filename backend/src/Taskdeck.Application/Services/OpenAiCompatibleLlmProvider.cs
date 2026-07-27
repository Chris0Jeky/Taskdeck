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
    private const string CircuitName = "OpenAICompatible";
    private const string BufferedStreamingFallbackReason =
        "Upstream endpoint rejected SSE streaming; the response was completed before emission.";

    private readonly HttpClient _httpClient;
    private readonly LlmProviderSettings _settings;
    private readonly ILogger<OpenAiCompatibleLlmProvider> _logger;
    private readonly CircuitBreakerStateTracker? _circuitBreakerTracker;
    private readonly CircuitBreakerSettings? _circuitBreakerSettings;
    private readonly bool _allowLocalhostEndpoints;

    // Retain the original public constructor for already-compiled consumers.
    public OpenAiCompatibleLlmProvider(
        HttpClient httpClient,
        LlmProviderSettings settings,
        ILogger<OpenAiCompatibleLlmProvider> logger)
        : this(httpClient, settings, logger, null, null, false)
    {
    }

    public OpenAiCompatibleLlmProvider(
        HttpClient httpClient,
        LlmProviderSettings settings,
        ILogger<OpenAiCompatibleLlmProvider> logger,
        CircuitBreakerStateTracker? circuitBreakerTracker,
        CircuitBreakerSettings? circuitBreakerSettings,
        bool allowLocalhostEndpoints)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _circuitBreakerTracker = circuitBreakerTracker;
        _circuitBreakerSettings = circuitBreakerSettings;
        _allowLocalhostEndpoints = allowLocalhostEndpoints;
    }

    public async Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
    {
        var userMessage = GetLastUserMessage(request);
        if (!TryValidateSettings(out var validationError))
        {
            _logger.LogWarning("OpenAI-compatible provider configuration invalid: {Error}", validationError);
            return BuildFallbackResult(userMessage, "Live provider configuration is invalid.");
        }

        using var timeout = CreateProviderTimeoutTokenSource(ct);
        try
        {
            var useInstructionExtraction = request.SystemPrompt is null;
            var response = await SendCompletionAsync(request, includeResponseFormat: useInstructionExtraction, timeout.Token);
            if (!response.IsSuccessStatusCode && useInstructionExtraction && IsResponseFormatRejection(response.StatusCode))
            {
                response.Dispose();
                _logger.LogInformation("OpenAI-compatible endpoint rejected response_format; retrying with prompt-enforced JSON only.");
                response = await SendCompletionAsync(request, includeResponseFormat: false, timeout.Token);
            }

            using (response)
            {
                var body = await ReadBoundedContentAsync(
                    response.Content,
                    _settings.OpenAiCompatible.MaxResponseBytes,
                    timeout.Token);
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

                var finishDegradation = GetFinishReasonDegradation(finishReason);
                if (finishDegradation is not null ||
                    (useInstructionExtraction && OpenAiLlmProvider.LooksLikeTruncatedJson(content)))
                {
                    return new LlmCompletionResult(
                        content, tokensUsed, false, Provider: ProviderName, Model: GetConfiguredModelOrDefault(),
                        IsDegraded: true,
                        DegradedReason: finishDegradation ?? "Response was truncated.");
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("OpenAI-compatible completion exceeded its configured response deadline.");
            return BuildFallbackResult(userMessage, "Live provider request timed out.");
        }
        catch (LlmProviderResponseLimitException ex)
        {
            _logger.LogWarning("OpenAI-compatible completion exceeded a response budget: {Limit}", ex.Message);
            return BuildFallbackResult(userMessage, "Live provider response exceeded a safety limit.");
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
        if (!TryValidateSettings(out var validationError))
        {
            yield return TerminalError($"Live provider configuration is invalid: {validationError}");
            yield break;
        }

        if (_circuitBreakerTracker is not null && _circuitBreakerSettings is not null &&
            !_circuitBreakerTracker.TryEnterStreamingRequest(CircuitName, _circuitBreakerSettings, out var circuitError))
        {
            yield return TerminalError(circuitError ?? "OpenAI-compatible streaming circuit is open.");
            yield break;
        }

        using var timeout = CreateProviderTimeoutTokenSource(ct);
        await using var enumerator = StreamCoreAsync(request, timeout.Token).GetAsyncEnumerator(timeout.Token);
        var emittedTerminal = false;

        while (true)
        {
            LlmTokenEvent? failure = null;
            bool moved;
            try
            {
                moved = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                moved = false;
                failure = TerminalError("OpenAI-compatible SSE response timed out after headers were received.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "OpenAI-compatible SSE response failed after request dispatch. {ExceptionSummary}",
                    SensitiveDataRedactor.SummarizeException(ex));
                moved = false;
                failure = TerminalError(ex is LlmProviderResponseLimitException
                    ? "OpenAI-compatible SSE response exceeded a safety limit."
                    : "OpenAI-compatible SSE transport failed before completion.");
            }

            if (failure is not null)
            {
                RecordStreamingFailure(failure.Error!);
                yield return failure;
                yield break;
            }

            if (!moved)
                break;

            var current = enumerator.Current;
            if (current.IsComplete)
            {
                emittedTerminal = true;
                if (IsStreamingFailure(current))
                    RecordStreamingFailure(current.Error ?? current.DegradedReason ?? "Degraded SSE completion.");
                else
                    RecordStreamingSuccess();
            }

            yield return current;
            if (current.IsComplete)
                yield break;
        }

        if (!emittedTerminal)
        {
            var failure = TerminalError("OpenAI-compatible SSE stream ended before a completion marker.");
            RecordStreamingFailure(failure.Error!);
            yield return failure;
        }
    }

    private async IAsyncEnumerable<LlmTokenEvent> StreamCoreAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Streaming chat is conversational text, not the buffered instruction-extraction
        // contract. Use this same effective request for a rejected-stream fallback.
        var streamingRequest = request.SystemPrompt is null ? request with { SystemPrompt = string.Empty } : request;
        using var message = CreateRequestMessage(streamingRequest, stream: true, includeResponseFormat: false);
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            if (IsStreamingRejection(response.StatusCode))
            {
                await foreach (var fallback in EmitBufferedFallbackAsync(streamingRequest, ct))
                    yield return fallback;
                yield break;
            }

            yield return TerminalError(
                $"OpenAI-compatible streaming request failed with status code {(int)response.StatusCode}.");
            yield break;
        }

        var compatible = _settings.OpenAiCompatible;
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var body = await ReadBoundedContentAsync(response.Content, compatible.MaxResponseBytes, ct);
            if (TryParseResponse(body, out var content, out var tokensUsed, out var finishReason))
            {
                var finishDegradation = GetFinishReasonDegradation(finishReason);
                yield return new LlmTokenEvent(
                    content,
                    true,
                    TokensUsed: tokensUsed,
                    Provider: ProviderName,
                    Model: GetConfiguredModelOrDefault())
                {
                    IsDegraded = true,
                    DegradedReason = finishDegradation is null
                        ? BufferedStreamingFallbackReason
                        : $"{BufferedStreamingFallbackReason} {finishDegradation}"
                };
            }
            else
            {
                yield return TerminalError(
                    "OpenAI-compatible endpoint returned a non-SSE response that could not be parsed.");
            }
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        var eventData = new StringBuilder();
        var eventBytes = 0;
        var responseBytes = 0;
        string? finishReasonSeen = null;
        int? tokensUsedSeen = null;

        while (true)
        {
            var line = await ReadBoundedLineAsync(reader, compatible.MaxSseLineBytes, ct);
            if (line.Text is null)
                break;

            responseBytes = checked(responseBytes + line.Utf8Bytes + 1);
            if (responseBytes > compatible.MaxResponseBytes)
                throw new LlmProviderResponseLimitException("aggregate SSE response byte budget exceeded");

            if (line.Text.Length == 0)
            {
                if (eventData.Length == 0)
                    continue;

                var parsed = TryParseSseEvent(eventData.ToString());
                eventData.Clear();
                eventBytes = 0;

                if (parsed.Error is not null)
                {
                    yield return TerminalError(parsed.Error);
                    yield break;
                }

                if (parsed.TokensUsed is not null)
                    tokensUsedSeen = parsed.TokensUsed;
                if (parsed.FinishReason is not null)
                    finishReasonSeen = parsed.FinishReason;
                if (!string.IsNullOrEmpty(parsed.Delta))
                    yield return new LlmTokenEvent(parsed.Delta, false, Provider: ProviderName, Model: GetConfiguredModelOrDefault());

                if (parsed.IsDoneMarker)
                {
                    yield return BuildTerminalCompletion(finishReasonSeen, tokensUsedSeen);
                    yield break;
                }

                continue;
            }

            if (!line.Text.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var dataField = line.Text[5..].TrimStart();
            var fieldBytes = Encoding.UTF8.GetByteCount(dataField);
            eventBytes = checked(eventBytes + fieldBytes + (eventData.Length > 0 ? 1 : 0));
            if (eventBytes > compatible.MaxSseEventBytes)
                throw new LlmProviderResponseLimitException("single SSE event byte budget exceeded");
            if (eventData.Length > 0)
                eventData.Append('\n');
            eventData.Append(dataField);
        }

        // Accept a final data field without the conventional blank separator.
        if (eventData.Length > 0)
        {
            var parsed = TryParseSseEvent(eventData.ToString());
            if (parsed.Error is not null)
            {
                yield return TerminalError(parsed.Error);
                yield break;
            }
            if (parsed.TokensUsed is not null)
                tokensUsedSeen = parsed.TokensUsed;
            if (parsed.FinishReason is not null)
                finishReasonSeen = parsed.FinishReason;
            if (!string.IsNullOrEmpty(parsed.Delta))
                yield return new LlmTokenEvent(parsed.Delta, false, Provider: ProviderName, Model: GetConfiguredModelOrDefault());
            if (parsed.IsDoneMarker)
            {
                yield return BuildTerminalCompletion(finishReasonSeen, tokensUsedSeen);
                yield break;
            }
        }

        yield return TerminalError(
            "OpenAI-compatible SSE stream ended before a completion marker.",
            tokensUsedSeen);
    }

    public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        var isValid = TryValidateSettings(out var error);
        return Task.FromResult(new LlmHealthStatus(isValid, ProviderName, isValid ? null : error, GetConfiguredModelOrDefault()));
    }

    public async Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await CompleteAsync(new ChatCompletionRequest(
                [new ChatCompletionMessage("user", "Reply with exactly: OK")],
                MaxTokens: 4,
                Temperature: 0,
                SystemPrompt: string.Empty), ct);
            return new LlmHealthStatus(
                !result.IsDegraded,
                ProviderName,
                result.IsDegraded ? result.DegradedReason : null,
                GetConfiguredModelOrDefault(),
                IsProbed: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new LlmHealthStatus(
                false,
                ProviderName,
                "Provider probe timed out.",
                GetConfiguredModelOrDefault(),
                IsProbed: true);
        }
    }

    private async IAsyncEnumerable<LlmTokenEvent> EmitBufferedFallbackAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var result = await CompleteAsync(request, ct);
        yield return new LlmTokenEvent(
            result.Content,
            true,
            TokensUsed: result.TokensUsed,
            Provider: ProviderName,
            Model: result.Model)
        {
            IsDegraded = true,
            DegradedReason = result.IsDegraded
                ? $"{BufferedStreamingFallbackReason} {result.DegradedReason}"
                : BufferedStreamingFallbackReason
        };
    }

    private async Task<HttpResponseMessage> SendCompletionAsync(
        ChatCompletionRequest request,
        bool includeResponseFormat,
        CancellationToken ct)
    {
        using var message = CreateRequestMessage(request, stream: false, includeResponseFormat);
        return await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private bool TryValidateSettings(out string error)
    {
        if (!_settings.EnableLiveProviders)
        {
            error = "Live providers are disabled.";
            return false;
        }
        if (!string.Equals(_settings.Provider, "OpenAiCompatible", StringComparison.OrdinalIgnoreCase))
        {
            error = "OpenAI-compatible is not the selected provider.";
            return false;
        }
        return LlmProviderSelectionPolicy.TryValidateOpenAiCompatibleSettings(
            _settings,
            out error,
            _allowLocalhostEndpoints);
    }

    private HttpRequestMessage CreateRequestMessage(ChatCompletionRequest request, bool stream, bool includeResponseFormat)
    {
        var compatible = _settings.OpenAiCompatible;
        var message = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsEndpoint());
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", compatible.ApiKey.Trim());
        LlmRequestAttributionMapper.AddAttributionHeaders(message, request.Attribution);
        foreach (var (name, value) in compatible.ExtraHeaders)
            message.Headers.Add(name, value);
        message.Content = JsonContent.Create(BuildRequestPayload(request, stream, includeResponseFormat));
        return message;
    }

    private object BuildRequestPayload(ChatCompletionRequest request, bool stream, bool includeResponseFormat)
    {
        var systemPrompt = LlmSystemPromptBuilder.BuildEffectiveSystemPrompt(
            request.SystemPrompt ?? LlmInstructionExtractionPrompt.SystemPrompt,
            request.BoardContext);
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
        if (stream)
            payload["stream_options"] = new { include_usage = true };
        if (includeResponseFormat)
            payload["response_format"] = new { type = "json_object" };
        if (request.Attribution is not null)
            payload["user"] = LlmRequestAttributionMapper.BuildUserToken(request.Attribution.UserId);
        return payload;
    }

    private static SseEventParseResult TryParseSseEvent(string data)
    {
        if (string.Equals(data.Trim(), "[DONE]", StringComparison.Ordinal))
            return new SseEventParseResult(IsDoneMarker: true);

        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new SseEventParseResult(Error: "OpenAI-compatible SSE event root was not an object.");
            if (root.TryGetProperty("error", out _))
                return new SseEventParseResult(Error: "OpenAI-compatible SSE stream reported an upstream error.");

            int? tokensUsed = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.ValueKind != JsonValueKind.Null &&
                    (usage.ValueKind != JsonValueKind.Object ||
                     !usage.TryGetProperty("total_tokens", out var total) ||
                     total.ValueKind != JsonValueKind.Number ||
                     !total.TryGetInt32(out var parsedTokens) || parsedTokens < 0))
                {
                    return new SseEventParseResult(Error: "OpenAI-compatible SSE usage metadata was malformed.");
                }
                if (usage.ValueKind == JsonValueKind.Object)
                    tokensUsed = usage.GetProperty("total_tokens").GetInt32();
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                return new SseEventParseResult(Error: "OpenAI-compatible SSE event did not contain a choices array.");
            if (choices.GetArrayLength() == 0)
                return tokensUsed is not null
                    ? new SseEventParseResult(TokensUsed: tokensUsed)
                    : new SseEventParseResult(Error: "OpenAI-compatible SSE event contained no choices or usage.");

            var first = choices[0];
            if (first.ValueKind != JsonValueKind.Object)
                return new SseEventParseResult(Error: "OpenAI-compatible SSE choice was not an object.");

            string? delta = null;
            if (first.TryGetProperty("delta", out var deltaElement))
            {
                if (deltaElement.ValueKind != JsonValueKind.Object)
                    return new SseEventParseResult(Error: "OpenAI-compatible SSE delta was not an object.");
                if (deltaElement.TryGetProperty("content", out var contentElement))
                {
                    if (contentElement.ValueKind == JsonValueKind.String)
                        delta = contentElement.GetString();
                    else if (contentElement.ValueKind != JsonValueKind.Null)
                        return new SseEventParseResult(Error: "OpenAI-compatible SSE delta content was not text.");
                }
            }

            string? finishReason = null;
            if (first.TryGetProperty("finish_reason", out var finish))
            {
                if (finish.ValueKind == JsonValueKind.String)
                    finishReason = finish.GetString();
                else if (finish.ValueKind != JsonValueKind.Null)
                    return new SseEventParseResult(Error: "OpenAI-compatible SSE finish_reason was not text or null.");
            }

            return new SseEventParseResult(delta, FinishReason: finishReason, TokensUsed: tokensUsed);
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
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0 ||
                choices[0].ValueKind != JsonValueKind.Object)
                return false;
            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
                return false;
            content = contentElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
                return false;

            if (choice.TryGetProperty("finish_reason", out var finish))
            {
                if (finish.ValueKind == JsonValueKind.String)
                    finishReason = finish.GetString();
                else if (finish.ValueKind != JsonValueKind.Null)
                    return false;
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.ValueKind != JsonValueKind.Object ||
                    !usage.TryGetProperty("total_tokens", out var total) ||
                    total.ValueKind != JsonValueKind.Number ||
                    !total.TryGetInt32(out tokensUsed) || tokensUsed < 0)
                    return false;
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

    private static async Task<string> ReadBoundedContentAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken ct)
    {
        if (content.Headers.ContentLength is long length && length > maxBytes)
            throw new LlmProviderResponseLimitException("Content-Length exceeded the response byte budget");

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(Math.Min(maxBytes, 16_384));
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), ct);
            if (read == 0)
                break;
            if (buffer.Length + read > maxBytes)
                throw new LlmProviderResponseLimitException("response body exceeded the response byte budget");
            buffer.Write(chunk, 0, read);
        }

        return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private static async Task<BoundedLine> ReadBoundedLineAsync(
        StreamReader reader,
        int maxBytes,
        CancellationToken ct)
    {
        var line = new StringBuilder();
        var one = new char[1];
        var bytes = 0;
        while (true)
        {
            var read = await reader.ReadAsync(one.AsMemory(), ct);
            if (read == 0)
                return line.Length == 0 ? new BoundedLine(null, 0) : new BoundedLine(line.ToString().TrimEnd('\r'), bytes);
            if (one[0] == '\n')
                return new BoundedLine(line.ToString().TrimEnd('\r'), bytes);

            bytes = checked(bytes + Encoding.UTF8.GetByteCount(one));
            if (bytes > maxBytes)
                throw new LlmProviderResponseLimitException("single SSE line byte budget exceeded");
            line.Append(one[0]);
        }
    }

    private LlmTokenEvent BuildTerminalCompletion(string? finishReason, int? tokensUsed)
    {
        if (string.IsNullOrWhiteSpace(finishReason))
            return TerminalError("OpenAI-compatible SSE stream completed without a finish reason.", tokensUsed);

        var degradation = GetFinishReasonDegradation(finishReason);
        return new LlmTokenEvent(
            string.Empty,
            true,
            TokensUsed: tokensUsed,
            Provider: ProviderName,
            Model: GetConfiguredModelOrDefault())
        {
            IsDegraded = degradation is not null,
            DegradedReason = degradation
        };
    }

    private LlmTokenEvent TerminalError(string error, int? tokensUsed = null) => new(
        string.Empty,
        true,
        Error: error,
        TokensUsed: tokensUsed,
        Provider: ProviderName,
        Model: GetConfiguredModelOrDefault());

    private CancellationTokenSource CreateProviderTimeoutTokenSource(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.OpenAiCompatible.TimeoutSeconds));
        return timeout;
    }

    private void RecordStreamingFailure(string reason)
    {
        if (_circuitBreakerTracker is not null && _circuitBreakerSettings is not null)
            _circuitBreakerTracker.RecordStreamingFailure(CircuitName, _circuitBreakerSettings, reason);
    }

    private void RecordStreamingSuccess()
    {
        _circuitBreakerTracker?.RecordStreamingSuccess(CircuitName);
    }

    private static bool IsStreamingFailure(LlmTokenEvent terminal) =>
        terminal.Error is not null ||
        (terminal.IsDegraded && !string.Equals(
            terminal.DegradedReason,
            BufferedStreamingFallbackReason,
            StringComparison.Ordinal));

    private static string? GetFinishReasonDegradation(string? finishReason)
    {
        if (string.Equals(finishReason, "stop", StringComparison.OrdinalIgnoreCase))
            return null;
        return finishReason?.Trim().ToLowerInvariant() switch
        {
            "length" => "Response was truncated because the upstream token limit was reached.",
            "content_filter" => "Response was stopped by the upstream content filter.",
            "tool_calls" => "Response ended with unsupported upstream tool calls.",
            "function_call" => "Response ended with an unsupported upstream function call.",
            null or "" => null,
            _ => "Response ended with a non-standard upstream finish reason."
        };
    }

    private static bool IsResponseFormatRejection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;

    private static bool IsStreamingRejection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or
            HttpStatusCode.NotImplemented or HttpStatusCode.UnprocessableEntity;

    private Uri BuildChatCompletionsEndpoint()
    {
        var baseUrl = _settings.OpenAiCompatible.BaseUrl.Trim().TrimEnd('/') + "/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), "chat/completions");
    }

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
        return new LlmCompletionResult(
            content,
            EstimateTokens(userMessage) + EstimateTokens(content),
            isActionable,
            actionIntent,
            ProviderName,
            GetConfiguredModelOrDefault(),
            true,
            reason,
            instructions.Count > 0 ? instructions : null);
    }

    private sealed record SseEventParseResult(
        string? Delta = null,
        bool IsDoneMarker = false,
        string? FinishReason = null,
        int? TokensUsed = null,
        string? Error = null);

    private sealed record BoundedLine(string? Text, int Utf8Bytes);

    private sealed class LlmProviderResponseLimitException(string message) : IOException(message);
}
