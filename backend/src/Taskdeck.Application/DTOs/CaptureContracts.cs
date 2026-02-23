using System.Text.Json;
using System.Text.Json.Serialization;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.DTOs;

public record CapturePayloadV1(
    int Version,
    CaptureSource Source,
    string Text,
    DateTimeOffset? ClientCreatedAt = null,
    string? TitleHint = null,
    string? ExternalRef = null,
    CaptureProvenanceV1? Provenance = null);

public record CaptureProvenanceV1(
    Guid CaptureItemId,
    Guid? TriageRunId = null,
    Guid? ProposalId = null,
    string? PromptVersion = null);

public static class CaptureRequestContract
{
    public const string RequestTypePrefix = "inbox.capture.";
    public const string RequestTypeV1 = "inbox.capture.v1";
    public const int CurrentSchemaVersion = 1;
    public const int MaxRawTextLength = 20_000;
    public const int MaxTitleHintLength = 240;
    public const int MaxExternalRefLength = 2_048;
    public const int MaxPromptVersionLength = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static bool IsCaptureRequestType(string requestType)
    {
        return !string.IsNullOrWhiteSpace(requestType)
               && requestType.StartsWith(RequestTypePrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static Result ValidateRequestType(string requestType)
    {
        if (string.IsNullOrWhiteSpace(requestType))
        {
            return Result.Failure(ErrorCodes.ValidationError, "Request type cannot be empty");
        }

        var normalized = requestType.Trim();
        if (!IsCaptureRequestType(normalized))
        {
            return Result.Success();
        }

        if (!string.Equals(normalized, RequestTypeV1, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"Unsupported capture request type '{requestType}'. Supported type: {RequestTypeV1}");
        }

        return Result.Success();
    }

    public static Result<CapturePayloadV1> ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure<CapturePayloadV1>(ErrorCodes.ValidationError, "Capture payload cannot be empty");
        }

        // Backward-compatible fallback for legacy/plaintext queue payloads.
        if (!LooksLikeJsonObject(payload))
        {
            return ValidatePayload(new CapturePayloadV1(CurrentSchemaVersion, CaptureSource.Typed, payload));
        }

        try
        {
            using var jsonDocument = JsonDocument.Parse(payload);
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure<CapturePayloadV1>(ErrorCodes.ValidationError, "Capture payload JSON must be an object");
            }

            var identityFieldValidation = ValidateForbiddenIdentityFields(jsonDocument.RootElement);
            if (!identityFieldValidation.IsSuccess)
            {
                return Result.Failure<CapturePayloadV1>(identityFieldValidation.ErrorCode, identityFieldValidation.ErrorMessage);
            }

            var wire = JsonSerializer.Deserialize<CapturePayloadWireModel>(payload, JsonOptions);
            if (wire == null)
            {
                return Result.Failure<CapturePayloadV1>(ErrorCodes.ValidationError, "Capture payload JSON is invalid");
            }

            if (wire.Version.HasValue && wire.Version.Value != CurrentSchemaVersion)
            {
                return Result.Failure<CapturePayloadV1>(
                    ErrorCodes.ValidationError,
                    $"Unsupported capture payload schema version '{wire.Version.Value}'. Supported version: {CurrentSchemaVersion}");
            }

            if (!TryParseSource(wire.Source, out var source, out var sourceError))
            {
                return Result.Failure<CapturePayloadV1>(ErrorCodes.ValidationError, sourceError);
            }

            var payloadModel = new CapturePayloadV1(
                CurrentSchemaVersion,
                source,
                wire.Text ?? string.Empty,
                wire.ClientCreatedAt,
                wire.TitleHint,
                wire.ExternalRef,
                wire.Provenance);

            return ValidatePayload(payloadModel);
        }
        catch (JsonException)
        {
            return Result.Failure<CapturePayloadV1>(ErrorCodes.ValidationError, "Capture payload JSON is invalid");
        }
    }

    public static Result<CapturePayloadV1> ValidatePayload(CapturePayloadV1 payload)
    {
        if (payload.Version != CurrentSchemaVersion)
        {
            return Result.Failure<CapturePayloadV1>(
                ErrorCodes.ValidationError,
                $"Capture payload version must be {CurrentSchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(payload.Text))
        {
            return Result.Failure<CapturePayloadV1>(ErrorCodes.ValidationError, "Capture text cannot be empty");
        }

        if (payload.Text.Length > MaxRawTextLength)
        {
            return Result.Failure<CapturePayloadV1>(
                ErrorCodes.ValidationError,
                $"Capture text cannot exceed {MaxRawTextLength} characters");
        }

        if (payload.TitleHint?.Length > MaxTitleHintLength)
        {
            return Result.Failure<CapturePayloadV1>(
                ErrorCodes.ValidationError,
                $"Capture title hint cannot exceed {MaxTitleHintLength} characters");
        }

        if (payload.ExternalRef?.Length > MaxExternalRefLength)
        {
            return Result.Failure<CapturePayloadV1>(
                ErrorCodes.ValidationError,
                $"Capture external reference cannot exceed {MaxExternalRefLength} characters");
        }

        if (payload.Provenance?.PromptVersion?.Length > MaxPromptVersionLength)
        {
            return Result.Failure<CapturePayloadV1>(
                ErrorCodes.ValidationError,
                $"Capture prompt version cannot exceed {MaxPromptVersionLength} characters");
        }

        return Result.Success(payload);
    }

    public static CapturePayloadV1 WithProvenance(
        CapturePayloadV1 payload,
        Guid captureItemId,
        Guid? triageRunId = null,
        Guid? proposalId = null,
        string? promptVersion = null)
    {
        return payload with
        {
            Provenance = new CaptureProvenanceV1(captureItemId, triageRunId, proposalId, promptVersion)
        };
    }

    public static string SerializePayload(CapturePayloadV1 payload)
    {
        var validation = ValidatePayload(payload);
        if (!validation.IsSuccess)
        {
            throw new DomainException(validation.ErrorCode, validation.ErrorMessage ?? "Invalid capture payload");
        }

        return JsonSerializer.Serialize(validation.Value, JsonOptions);
    }

    private static bool LooksLikeJsonObject(string payload)
    {
        var trimmed = payload.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal);
    }

    private static bool TryParseSource(string? source, out CaptureSource parsedSource, out string error)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            parsedSource = CaptureSource.Typed;
            error = string.Empty;
            return true;
        }

        if (Enum.TryParse<CaptureSource>(source, true, out parsedSource))
        {
            error = string.Empty;
            return true;
        }

        error = $"Invalid capture source '{source}'";
        return false;
    }

    private static Result ValidateForbiddenIdentityFields(JsonElement root)
    {
        var forbiddenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "userId",
            "ownerUserId",
            "requestedByUserId",
            "actorUserId",
            "actorId"
        };

        foreach (var property in root.EnumerateObject())
        {
            if (forbiddenFields.Contains(property.Name) && property.Value.ValueKind != JsonValueKind.Null)
            {
                return Result.Failure(
                    ErrorCodes.ValidationError,
                    $"Capture payload must not include actor identity field '{property.Name}'");
            }
        }

        return Result.Success();
    }

    private sealed class CapturePayloadWireModel
    {
        public int? Version { get; init; }
        public string? Source { get; init; }
        public string? Text { get; init; }
        public DateTimeOffset? ClientCreatedAt { get; init; }
        public string? TitleHint { get; init; }
        public string? ExternalRef { get; init; }
        public CaptureProvenanceV1? Provenance { get; init; }
    }
}
