using System.Globalization;
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

/// <summary>
/// LLM-backed transcript triage output. Version two deliberately remains separate from the
/// deterministic v1 contract: model-reported classification and hints must never be fabricated
/// for the deterministic fallback.
/// </summary>
public sealed record CaptureTriageOutputV2(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string PromptVersion,
    [property: JsonRequired] IReadOnlyList<CaptureTriageTaskV2> Tasks);

/// <summary>
/// A model-extracted transcript item. Hints are descriptive only at this boundary; resolving an
/// assignee, applying a due date, and persisting evidence spans remain explicit review/persistence
/// work owned by later revival milestones.
/// </summary>
public sealed record CaptureTriageTaskV2(
    [property: JsonRequired] string Title,
    [property: JsonRequired] string Type,
    [property: JsonRequired] string? AssigneeHint,
    [property: JsonRequired] string? DueDateHint,
    [property: JsonRequired] decimal Confidence,
    [property: JsonRequired] string EvidenceQuote);

public static class CaptureTriageOutputContract
{
    /// <summary>Schema version used by the deterministic and historical LLM v1 contract.</summary>
    public const int SchemaVersion = 1;
    public const int SchemaVersionV2 = 2;
    public const string PromptVersionV1 = "triage.v1";

    /// <summary>
    /// Prompt version for LLM-backed transcript triage (REVIVAL-08 M1). The output shape is the
    /// same v1 schema; the prompt version distinguishes which extraction engine produced it so
    /// provenance stays honest (#1273). Schema file: capture-triage-output.llm-v1.schema.json.
    /// </summary>
    public const string PromptVersionLlmV1 = "llm-triage.v1";

    /// <summary>
    /// Prompt version for schema-v2 LLM transcript triage. The deterministic extractor remains on
    /// <see cref="PromptVersionV1"/> so its output never claims model-only metadata.
    /// </summary>
    public const string PromptVersionLlmV2 = "llm-triage.v2";

    public const int MaxTasks = 20;
    public const int MaxTaskTitleLength = 180;
    public const int MaxTaskEvidenceLength = 280;
    public const int MaxTaskAssigneeHintLength = 100;
    public const int DueDateHintLength = 10;

    /// <summary>
    /// Plausibility window for a model-supplied due-date hint, measured from the capture's
    /// reference date (#2193). A prompt that carries no reference date leaves the model free to
    /// invent a year — the shipped 2026-08-29 acceptance run returned 2023-09-01 for "Monday 1
    /// September" — and a format-only check let it through onto a card. The window is deliberately
    /// wide: it exists to catch a fabricated year, not to second-guess a distant real deadline.
    /// </summary>
    public const int MaxDueDateYearsBeforeReference = 2;

    /// <inheritdoc cref="MaxDueDateYearsBeforeReference"/>
    public const int MaxDueDateYearsAfterReference = 5;

    /// <summary>Longest model-supplied hint echoed back in a dropped-due-date note.</summary>
    private const int MaxDueDateHintNoteLength = 40;

    private static readonly string[] KnownPromptVersions = [PromptVersionV1, PromptVersionLlmV1];
    private static readonly string[] KnownTaskTypes = ["action", "decision", "question"];

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

    public static Result<CaptureTriageOutputV2> ParseAndValidateV2(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Failure<CaptureTriageOutputV2>(
                ErrorCodes.ValidationError,
                "Capture triage output cannot be empty");
        }

        CaptureTriageOutputV2? output;
        try
        {
            output = JsonSerializer.Deserialize<CaptureTriageOutputV2>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Failure<CaptureTriageOutputV2>(
                ErrorCodes.ValidationError,
                "Capture triage output JSON is invalid");
        }

        if (output is null)
        {
            return Result.Failure<CaptureTriageOutputV2>(
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
            if (task is null)
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} cannot be null");
            }

            if (string.IsNullOrWhiteSpace(task.Title))
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} title cannot be empty");
            }

            if (task.Title.Length > MaxTaskTitleLength)
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} title cannot exceed {MaxTaskTitleLength} characters");
            }

            if (string.IsNullOrWhiteSpace(task.Evidence))
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} evidence cannot be empty");
            }

            if (task.Evidence.Length > MaxTaskEvidenceLength)
            {
                return Result.Failure<CaptureTriageOutputV1>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} evidence cannot exceed {MaxTaskEvidenceLength} characters");
            }
        }

        return Result.Success(output);
    }

    /// <summary>
    /// Validates a schema-v2 output. When <paramref name="referenceDate"/> is supplied, a due-date
    /// hint outside the plausibility window around it is rejected as well as format-checked
    /// (#2193). It stays optional so callers that hold no capture day keep the format-only
    /// contract they had; the live extraction path drops such a hint earlier, in
    /// <c>LlmCaptureTriagePrompt.TryParseTasks</c>, so one bad date never costs the whole run.
    /// </summary>
    public static Result<CaptureTriageOutputV2> Validate(
        CaptureTriageOutputV2 output,
        DateOnly? referenceDate = null)
    {
        if (output.Version != SchemaVersionV2)
        {
            return Result.Failure<CaptureTriageOutputV2>(
                ErrorCodes.ValidationError,
                $"Capture triage output version must be {SchemaVersionV2}");
        }

        if (!string.Equals(output.PromptVersion, PromptVersionLlmV2, StringComparison.Ordinal))
        {
            return Result.Failure<CaptureTriageOutputV2>(
                ErrorCodes.ValidationError,
                $"Capture triage prompt version must be '{PromptVersionLlmV2}'");
        }

        if (output.Tasks is null || output.Tasks.Count == 0)
        {
            return Result.Failure<CaptureTriageOutputV2>(
                ErrorCodes.ValidationError,
                "Capture triage output must contain at least one task");
        }

        if (output.Tasks.Count > MaxTasks)
        {
            return Result.Failure<CaptureTriageOutputV2>(
                ErrorCodes.ValidationError,
                $"Capture triage output cannot contain more than {MaxTasks} tasks");
        }

        for (var i = 0; i < output.Tasks.Count; i++)
        {
            var task = output.Tasks[i];
            var index = i + 1;
            if (task is null)
            {
                return Result.Failure<CaptureTriageOutputV2>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} cannot be null");
            }

            if (string.IsNullOrWhiteSpace(task.Title))
            {
                return Result.Failure<CaptureTriageOutputV2>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} title cannot be empty");
            }

            if (task.Title.Length > MaxTaskTitleLength)
            {
                return Result.Failure<CaptureTriageOutputV2>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} title cannot exceed {MaxTaskTitleLength} characters");
            }

            if (string.IsNullOrWhiteSpace(task.Type) ||
                !KnownTaskTypes.Contains(task.Type, StringComparer.Ordinal))
            {
                return Result.Failure<CaptureTriageOutputV2>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} type must be one of: {string.Join(", ", KnownTaskTypes.Select(type => $"'{type}'"))}");
            }

            if (task.AssigneeHint is not null &&
                (string.IsNullOrWhiteSpace(task.AssigneeHint) || task.AssigneeHint.Length > MaxTaskAssigneeHintLength))
            {
                return Result.Failure<CaptureTriageOutputV2>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} assignee hint must be non-empty and cannot exceed {MaxTaskAssigneeHintLength} characters");
            }

            if (task.DueDateHint is not null)
            {
                if (!TryParseDueDateHint(task.DueDateHint, out var dueDate))
                {
                    return Result.Failure<CaptureTriageOutputV2>(
                        ErrorCodes.ValidationError,
                        $"Capture triage task {index} due date hint must use YYYY-MM-DD when provided");
                }

                if (referenceDate is DateOnly reference &&
                    !IsWithinDueDatePlausibilityWindow(dueDate, reference))
                {
                    return Result.Failure<CaptureTriageOutputV2>(
                        ErrorCodes.ValidationError,
                        $"Capture triage task {index} due date hint must fall between " +
                        $"{MaxDueDateYearsBeforeReference} years before and {MaxDueDateYearsAfterReference} years after " +
                        $"the reference date {reference.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
                }
            }

            if (task.Confidence is < 0m or > 1m)
            {
                return Result.Failure<CaptureTriageOutputV2>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} confidence must be between 0 and 1");
            }

            if (string.IsNullOrWhiteSpace(task.EvidenceQuote))
            {
                return Result.Failure<CaptureTriageOutputV2>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} evidence quote cannot be empty");
            }

            if (task.EvidenceQuote.Length > MaxTaskEvidenceLength)
            {
                return Result.Failure<CaptureTriageOutputV2>(
                    ErrorCodes.ValidationError,
                    $"Capture triage task {index} evidence quote cannot exceed {MaxTaskEvidenceLength} characters");
            }
        }

        return Result.Success(output);
    }

    /// <summary>
    /// Parses a model-supplied due-date hint in the one accepted form. Length is checked as well as
    /// the pattern so a padded or suffixed value cannot slip past <c>DateOnly.TryParseExact</c>.
    /// </summary>
    public static bool TryParseDueDateHint(string? dueDateHint, out DateOnly dueDate)
    {
        dueDate = default;
        return dueDateHint is not null &&
               dueDateHint.Length == DueDateHintLength &&
               DateOnly.TryParseExact(
                   dueDateHint,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out dueDate);
    }

    /// <summary>
    /// Whether a due date is close enough to the capture's reference date to have been read out of
    /// the transcript rather than invented. See <see cref="MaxDueDateYearsBeforeReference"/>.
    /// </summary>
    public static bool IsWithinDueDatePlausibilityWindow(DateOnly dueDate, DateOnly referenceDate) =>
        dueDate >= referenceDate.AddYears(-MaxDueDateYearsBeforeReference) &&
        dueDate <= referenceDate.AddYears(MaxDueDateYearsAfterReference);

    /// <summary>
    /// Applies the due-date rule to one model-supplied hint (#2193). Returns the hint unchanged
    /// when it is absent or usable, and null plus an honest note when it must be dropped: an
    /// unusable date is a hint on a reviewable proposal, so it is worth dropping but not worth
    /// discarding every other item the model extracted.
    /// </summary>
    /// <param name="taskNumber">1-based position of the task, matching validation error wording.</param>
    public static string? ReviewDueDateHint(
        string? dueDateHint,
        int taskNumber,
        DateOnly referenceDate,
        out string? note)
    {
        note = null;
        if (dueDateHint is null)
        {
            return null;
        }

        var reference = referenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (!TryParseDueDateHint(dueDateHint, out var dueDate))
        {
            note = $"Task {taskNumber}: dropped the due date '{TruncateDueDateHintForNote(dueDateHint)}' " +
                   "because it is not a YYYY-MM-DD calendar date.";
            return null;
        }

        if (!IsWithinDueDatePlausibilityWindow(dueDate, referenceDate))
        {
            note = $"Task {taskNumber}: dropped the due date '{dueDateHint}' because it is not within " +
                   $"{MaxDueDateYearsBeforeReference} years before or {MaxDueDateYearsAfterReference} years after " +
                   $"the capture date {reference}.";
            return null;
        }

        return dueDateHint;
    }

    /// <summary>
    /// An unparseable hint is arbitrary model text, so a note quoting it stays bounded and single
    /// line. Control characters are replaced rather than dropped so the length stays honest.
    /// </summary>
    private static string TruncateDueDateHintForNote(string dueDateHint)
    {
        var sanitized = string.Create(
            Math.Min(dueDateHint.Length, MaxDueDateHintNoteLength),
            dueDateHint,
            static (span, source) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = char.IsControl(source[i]) ? ' ' : source[i];
                }
            });

        return dueDateHint.Length > MaxDueDateHintNoteLength ? sanitized + "..." : sanitized;
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

    public static string Serialize(CaptureTriageOutputV2 output)
    {
        var validation = Validate(output);
        if (!validation.IsSuccess)
        {
            throw new DomainException(validation.ErrorCode, validation.ErrorMessage ?? "Invalid triage output");
        }

        return JsonSerializer.Serialize(validation.Value, JsonOptions);
    }
}
