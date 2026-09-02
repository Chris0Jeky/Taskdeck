using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Taskdeck.Acceleration.Candidates.Streaming;

public enum OpenAiStreamFrameKind
{
    Delta,
    Completed,
    Usage,
    ProviderError
}

public sealed record OpenAiStreamFrame(
    OpenAiStreamFrameKind Kind,
    long? ChoiceIndex = null,
    string? Text = null,
    string? FinishReason = null,
    long? PromptTokens = null,
    long? CompletionTokens = null,
    long? TotalTokens = null,
    string? ErrorCode = null,
    string? ErrorType = null);

/// <summary>
/// Decodes the OpenAI-compatible chat-completions SSE shape.
/// Provider error messages are intentionally not propagated because they may contain user content.
/// </summary>
public static class OpenAiStreamDecoder
{
    public static IReadOnlyList<OpenAiStreamFrame> Decode(SseEvent serverEvent)
    {
        if (string.Equals(serverEvent.Data.Trim(), "[DONE]", StringComparison.Ordinal))
        {
            return new[] { new OpenAiStreamFrame(OpenAiStreamFrameKind.Completed) };
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(serverEvent.Data);
        }
        catch (JsonException exception)
        {
            throw new OpenAiStreamProtocolException("openai_stream_json_invalid", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            var frames = new List<OpenAiStreamFrame>();

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                frames.Add(new OpenAiStreamFrame(
                    OpenAiStreamFrameKind.ProviderError,
                    ErrorCode: ReadString(error, "code"),
                    ErrorType: ReadString(error, "type")));
                return frames;
            }

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                frames.Add(new OpenAiStreamFrame(
                    OpenAiStreamFrameKind.Usage,
                    PromptTokens: ReadInt64(usage, "prompt_tokens"),
                    CompletionTokens: ReadInt64(usage, "completion_tokens"),
                    TotalTokens: ReadInt64(usage, "total_tokens")));
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            {
                return frames;
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.ValueKind != JsonValueKind.Object)
                {
                    throw new OpenAiStreamProtocolException("openai_stream_choice_invalid");
                }

                var choiceIndex = ReadInt64(choice, "index") ?? 0;
                if (choiceIndex < 0)
                {
                    throw new OpenAiStreamProtocolException("openai_stream_choice_index_invalid");
                }

                if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                {
                    var text = ReadString(delta, "content");
                    if (!string.IsNullOrEmpty(text))
                    {
                        frames.Add(new OpenAiStreamFrame(
                            OpenAiStreamFrameKind.Delta,
                            ChoiceIndex: choiceIndex,
                            Text: text));
                    }
                }

                var finishReason = ReadString(choice, "finish_reason");
                if (!string.IsNullOrEmpty(finishReason))
                {
                    frames.Add(new OpenAiStreamFrame(
                        OpenAiStreamFrameKind.Completed,
                        ChoiceIndex: choiceIndex,
                        FinishReason: finishReason));
                }
            }

            return frames;
        }
    }

    private static string? ReadString(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? ReadInt64(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt64(out var result)
            ? result
            : null;
    }
}

public sealed class OpenAiStreamProtocolException : Exception
{
    public OpenAiStreamProtocolException(string code, Exception? innerException = null)
        : base(code, innerException) => Code = code;

    public string Code { get; }
}
