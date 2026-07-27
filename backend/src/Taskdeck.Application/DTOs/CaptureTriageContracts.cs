using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.DTOs;

public sealed record CaptureTriageOutputV1(
    int Version,
    string PromptVersion,
    IReadOnlyList<CaptureTriageTaskV1> Tasks);

public sealed record CaptureTriageTaskV1(
    string Title,
    string Evidence);

public static class CaptureTriageOutputContract
{
    public const int SchemaVersion = 1;
    public const string PromptVersionV1 = "triage.v1";

    /// <summary>
    /// Prompt version for LLM-backed transcript triage (REVIVAL-08 M1). The output shape is the
    /// same v1 schema; the prompt version distinguishes which extraction engine produced it so
    /// provenance stays honest (#1273). Schema file: capture-triage-output.llm-v1.schema.json.
    /// </summary>
    public const string PromptVersionLlmV1 = "llm-triage.v1";

    /// <summary>
    /// Prompt version for collision-resistant untrusted-data framing, exact raw-JSON containment,
    /// and ordinal evidence grounding (#1323). Historical llm-v1 envelopes remain readable.
    /// Schema file: capture-triage-output.llm-v2.schema.json.
    /// </summary>
    public const string PromptVersionLlmV2 = "llm-triage.v2";

    public const int MaxTasks = 20;
    public const int MaxTaskTitleLength = 180;
    public const int MaxTaskEvidenceLength = 280;

    private static readonly string[] KnownPromptVersions =
        [PromptVersionV1, PromptVersionLlmV1, PromptVersionLlmV2];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static Result<CaptureTriageOutputV1> ParseAndValidate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Failure<CaptureTriageOutputV1>(
                ErrorCodes.ValidationError,
                "Capture triage output cannot be empty");
        }

        CaptureTriageOutputV1? output;
        try
        {
            output = JsonSerializer.Deserialize<CaptureTriageOutputV1>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Failure<CaptureTriageOutputV1>(
                ErrorCodes.ValidationError,
                "Capture triage output JSON is invalid");
        }

        if (output is null)
        {
            return Result.Failure<CaptureTriageOutputV1>(
                ErrorCodes.ValidationError,
                "Capture triage output JSON is invalid");
        }

        return Validate(output);
    }

    public static Result<CaptureTriageOutputV1> Validate(CaptureTriageOutputV1 output)
    {
        if (output.Version != SchemaVersion)
        {
            return Result.Failure<CaptureTriageOutputV1>(
                ErrorCodes.ValidationError,
                $"Capture triage output version must be {SchemaVersion}");
        }

        if (!KnownPromptVersions.Contains(output.PromptVersion, StringComparer.Ordinal))
        {
            return Result.Failure<CaptureTriageOutputV1>(
                ErrorCodes.ValidationError,
                $"Capture triage prompt version must be one of: {string.Join(", ", KnownPromptVersions.Select(v => $"'{v}'"))}");
        }

        if (output.Tasks is null || output.Tasks.Count == 0)
        {
            return Result.Failure<CaptureTriageOutputV1>(
                ErrorCodes.ValidationError,
                "Capture triage output must contain at least one task");
        }

        if (output.Tasks.Count > MaxTasks)
        {
            return Result.Failure<CaptureTriageOutputV1>(
                ErrorCodes.ValidationError,
                $"Capture triage output cannot contain more than {MaxTasks} tasks");
        }

        for (var i = 0; i < output.Tasks.Count; i++)
        {
            var task = output.Tasks[i];
            var index = i + 1;
            var usesLlmV2Contract = output.PromptVersion == PromptVersionLlmV2;
            if (task is null)
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} cannot be null");
            }

            if (usesLlmV2Contract
                    ? IsNullOrEcmaWhitespace(task.Title)
                    : string.IsNullOrWhiteSpace(task.Title))
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} title cannot be empty");
            }

            var titleLength = usesLlmV2Contract
                ? GetUnicodeScalarLength(task.Title)
                : task.Title.Length;
            if (titleLength > MaxTaskTitleLength)
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} title cannot exceed {MaxTaskTitleLength} characters");
            }

            if (usesLlmV2Contract && !IsSafeTaskTitle(task.Title))
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} title contains unsafe whitespace, control, or bidi characters");
            }

            if (usesLlmV2Contract
                    ? IsNullOrEcmaWhitespace(task.Evidence)
                    : string.IsNullOrWhiteSpace(task.Evidence))
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} evidence cannot be empty");
            }

            var evidenceLength = usesLlmV2Contract
                ? GetUnicodeScalarLength(task.Evidence)
                : task.Evidence.Length;
            if (evidenceLength > MaxTaskEvidenceLength)
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} evidence cannot exceed {MaxTaskEvidenceLength} characters");
            }
        }

        return Result.Success(output);
    }

    internal static bool IsSafeTaskTitle(string title)
    {
        if (string.IsNullOrEmpty(title) ||
            IsEcmaWhitespace(title[0]) ||
            IsEcmaWhitespace(title[^1]))
        {
            return false;
        }

        foreach (var character in title)
        {
            if (IsUnsafeTaskTitleCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    internal static string SanitizeTaskTitle(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        var sanitized = new string(title
            .Select(character => IsUnsafeTaskTitleCharacter(character) ? ' ' : character)
            .ToArray());
        return string.Join(
            " ",
            sanitized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    internal static bool IsNullOrEcmaWhitespace([NotNullWhen(false)] string? value)
    {
        return string.IsNullOrEmpty(value) || value.EnumerateRunes().All(IsEcmaWhitespace);
    }

    internal static int GetUnicodeScalarLength(string value)
    {
        return value.EnumerateRunes().Count();
    }

    internal static string TruncateToUtf16LengthAtScalarBoundary(string value, int maxUtf16Length)
    {
        if (maxUtf16Length <= 0 || string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= maxUtf16Length)
        {
            return value;
        }

        var utf16Length = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (utf16Length + rune.Utf16SequenceLength > maxUtf16Length)
            {
                break;
            }

            utf16Length += rune.Utf16SequenceLength;
        }

        return value[..utf16Length];
    }

    private static bool IsEcmaWhitespace(Rune rune)
    {
        return IsEcmaWhitespace(rune.Value);
    }

    private static bool IsEcmaWhitespace(int codePoint)
    {
        return codePoint is >= 0x0009 and <= 0x000D or
               0x0020 or 0x00A0 or 0x1680 or
               >= 0x2000 and <= 0x200A or
               0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 or 0xFEFF;
    }

    private static bool IsUnsafeTaskTitleCharacter(char character)
    {
        return char.IsControl(character) ||
               character == '\uFEFF' ||
               character == '\u2028' || character == '\u2029' ||
               character == '\u061C' || character == '\u200E' || character == '\u200F' ||
               (character >= '\u202A' && character <= '\u202E') ||
               (character >= '\u2066' && character <= '\u2069');
    }

    public static string Serialize(CaptureTriageOutputV1 output)
    {
        var validation = Validate(output);
        if (!validation.IsSuccess)
        {
            throw new DomainException(validation.ErrorCode, validation.ErrorMessage ?? "Invalid triage output");
        }

        return JsonSerializer.Serialize(validation.Value, JsonOptions);
    }
}
