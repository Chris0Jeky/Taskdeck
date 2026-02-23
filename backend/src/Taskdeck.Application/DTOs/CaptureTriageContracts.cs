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
    public const int MaxTasks = 20;
    public const int MaxTaskTitleLength = 180;
    public const int MaxTaskEvidenceLength = 280;

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

        if (!string.Equals(output.PromptVersion, PromptVersionV1, StringComparison.Ordinal))
        {
            return Result.Failure<CaptureTriageOutputV1>(
                ErrorCodes.ValidationError,
                $"Capture triage prompt version must be '{PromptVersionV1}'");
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
