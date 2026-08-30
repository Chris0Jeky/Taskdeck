using System.Text.Json;
using System.Text.Json.Serialization;
using Taskdeck.Domain.Processing;

namespace Taskdeck.Application.Processing.Protocol;

/// <summary>
/// Taskdeck Worker Protocol v1 (ADR-0065 §Decision 10; spec in
/// <c>docs/architecture/WORKER_PROTOCOL_V1.md</c>): JSON-RPC 2.0 envelopes exchanged between the
/// API and a supervised sidecar over stdio, or mapped onto a queue for hosted workers. This file is
/// the typed contract; the host, supervisor and conformance suite are CF-04 <c>#2258</c>.
/// </summary>
public static class WorkerProtocol
{
    public const int Version = 1;
    public const string JsonRpcVersion = "2.0";

    public const string RunMethod = "processor.run";
    public const string ProgressMethod = "processor.progress";
    public const string CancelMethod = "processor.cancel";
    public const string DescribeMethod = "processor.describe";

    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
    public const string StatusCancelled = "cancelled";

    public static readonly JsonSerializerOptions JsonOptions = ProcessorManifestJson.Options;

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);
}

/// <summary>JSON-RPC error codes reserved by the protocol (-32000..-32099 is the server-defined range).</summary>
public static class WorkerProtocolErrorCodes
{
    public const int ProcessorFailure = -32000;
    public const int UnsupportedCapability = -32010;
    public const int UnsupportedMediaType = -32011;
    public const int ResourceExhausted = -32020;
    public const int DeadlineExceeded = -32021;
    public const int OutputTooLarge = -32022;
    public const int Cancelled = -32030;
    public const int ProtocolVersionMismatch = -32040;
}

public sealed record JsonRpcRequest<TParams>(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    string Id,
    string Method,
    TParams Params)
{
    public static JsonRpcRequest<TParams> Create(string id, string method, TParams parameters) =>
        new(WorkerProtocol.JsonRpcVersion, id, method, parameters);
}

public sealed record JsonRpcNotification<TParams>(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    string Method,
    TParams Params)
{
    public static JsonRpcNotification<TParams> Create(string method, TParams parameters) =>
        new(WorkerProtocol.JsonRpcVersion, method, parameters);
}

public sealed record JsonRpcResponse<TResult>(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    string Id,
    TResult? Result,
    JsonRpcError? Error)
{
    public bool IsError => Error is not null;
}

public sealed record JsonRpcError(int Code, string Message, ProcessorErrorData? Data);

/// <summary>
/// Machine-readable failure detail. <c>SafeDetail</c> must be content-free: it may name a model or a
/// resource, never a fragment of the user's material.
/// </summary>
public sealed record ProcessorErrorData(string ErrorCode, bool Retryable, string? SafeDetail);

public sealed record ProcessorRunParams(
    int ProtocolVersion,
    string Capability,
    ProcessorRunInput? Input,
    ProcessorRunOptions? Options,
    ProcessorRunLimits? Limits);

/// <summary>
/// The input handle. Content reaches a sidecar only through a Taskdeck-managed spool handle
/// (<c>spool://…</c>) or a short-lived authenticated content handle — never an arbitrary path the
/// user supplied.
/// </summary>
public sealed record ProcessorRunInput(
    Guid AssetId,
    string MediaType,
    string ContentHandle,
    string Sha256,
    long ByteSize);

public sealed record ProcessorRunOptions(
    string? Language,
    string? QualityTier,
    bool? WordTimestamps,
    bool? Diarization,
    int? MaxSpeakers);

public sealed record ProcessorRunLimits(
    DateTimeOffset? DeadlineUtc,
    int? MaxWallTimeMs,
    long? MaxOutputBytes);

public sealed record ProcessorProgressParams(
    string JobId,
    string Phase,
    double? Fraction,
    string? MessageCode);

public sealed record ProcessorRunResult(
    string Status,
    ProcessorIdentity? Processor,
    IReadOnlyList<ProcessorRepresentationOutput>? Representations,
    IReadOnlyList<string>? Warnings,
    ProcessorUsage? Usage);

public sealed record ProcessorIdentity(
    string Id,
    string Version,
    string? Model,
    string? ConfigurationHash);

public sealed record ProcessorRepresentationOutput(
    string Kind,
    int SchemaVersion,
    string? Language,
    string Text,
    IReadOnlyList<ProcessorSegmentOutput>? Segments);

public sealed record ProcessorSegmentOutput(
    int CharStart,
    int CharEnd,
    long? StartMs,
    long? EndMs,
    string? SpeakerLabel,
    double? Confidence);

public sealed record ProcessorUsage(
    long? WallTimeMs,
    long? AudioDurationMs,
    int? PeakRamMb,
    int? PeakVramMb);

/// <summary>
/// Structural validation of protocol messages. Host-side enforcement of deadlines, output caps and
/// network denial belongs to the supervisor (CF-04); this validator only rejects malformed envelopes
/// before any work starts or any result is persisted.
/// </summary>
public static class WorkerProtocolValidator
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        WorkerProtocol.StatusCompleted, WorkerProtocol.StatusFailed, WorkerProtocol.StatusCancelled
    };

    public static IReadOnlyList<string> ValidateRunParams(ProcessorRunParams? parameters)
    {
        var errors = new List<string>();

        if (parameters is null)
        {
            errors.Add("params: missing");
            return errors;
        }

        if (parameters.ProtocolVersion != WorkerProtocol.Version)
            errors.Add($"params.protocolVersion: must be {WorkerProtocol.Version}");

        if (!ProcessingCapability.IsKnown(parameters.Capability))
            errors.Add($"params.capability: '{parameters.Capability}' is not a known capability");

        if (parameters.Input is null)
        {
            errors.Add("params.input: required");
        }
        else
        {
            var input = parameters.Input;
            if (input.AssetId == Guid.Empty)
                errors.Add("params.input.assetId: required");
            if (string.IsNullOrWhiteSpace(input.MediaType))
                errors.Add("params.input.mediaType: required");
            if (string.IsNullOrWhiteSpace(input.ContentHandle))
                errors.Add("params.input.contentHandle: required");
            if (input.Sha256 is null || input.Sha256.Length != 64 || input.Sha256.Any(character => !Uri.IsHexDigit(character)))
                errors.Add("params.input.sha256: must be a 64-character hexadecimal digest");
            if (input.ByteSize <= 0)
                errors.Add("params.input.byteSize: must be greater than zero");
        }

        if (parameters.Limits is { MaxWallTimeMs: <= 0 })
            errors.Add("params.limits.maxWallTimeMs: must be greater than zero");

        if (parameters.Limits is { MaxOutputBytes: <= 0 })
            errors.Add("params.limits.maxOutputBytes: must be greater than zero");

        if (parameters.Options is { MaxSpeakers: <= 0 })
            errors.Add("params.options.maxSpeakers: must be greater than zero");

        return errors;
    }

    public static IReadOnlyList<string> ValidateResult(ProcessorRunResult? result)
    {
        var errors = new List<string>();

        if (result is null)
        {
            errors.Add("result: missing");
            return errors;
        }

        if (result.Status is null || !Statuses.Contains(result.Status))
            errors.Add("result.status: one of completed | failed | cancelled");

        if (result.Processor is null)
        {
            errors.Add("result.processor: required");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(result.Processor.Id))
                errors.Add("result.processor.id: required");
            if (string.IsNullOrWhiteSpace(result.Processor.Version))
                errors.Add("result.processor.version: required");
        }

        if (result.Status == WorkerProtocol.StatusCompleted && (result.Representations is null || result.Representations.Count == 0))
            errors.Add("result.representations: a completed run must emit at least one representation");

        if (result.Representations is null)
            return errors;

        for (var index = 0; index < result.Representations.Count; index++)
        {
            var representation = result.Representations[index];
            var prefix = $"result.representations[{index}]";

            if (string.IsNullOrWhiteSpace(representation.Kind))
                errors.Add($"{prefix}.kind: required");
            if (representation.SchemaVersion < 1)
                errors.Add($"{prefix}.schemaVersion: must be at least 1");
            if (representation.Text is null)
                errors.Add($"{prefix}.text: required (may be empty, never absent)");

            if (representation.Segments is null)
                continue;

            var textLength = representation.Text?.Length ?? 0;
            var previousEnd = 0;
            for (var segmentIndex = 0; segmentIndex < representation.Segments.Count; segmentIndex++)
            {
                var segment = representation.Segments[segmentIndex];
                var segmentPrefix = $"{prefix}.segments[{segmentIndex}]";

                if (segment.CharStart < 0 || segment.CharEnd < segment.CharStart || segment.CharEnd > textLength)
                    errors.Add($"{segmentPrefix}: char range must satisfy 0 <= charStart <= charEnd <= text.length");
                if (segment.CharStart < previousEnd)
                    errors.Add($"{segmentPrefix}: segments must not overlap and must be ordered by charStart");
                if (segment.StartMs is < 0 || segment.EndMs is < 0)
                    errors.Add($"{segmentPrefix}: timestamps cannot be negative");
                if (segment is { StartMs: not null, EndMs: not null } && segment.EndMs < segment.StartMs)
                    errors.Add($"{segmentPrefix}: endMs cannot precede startMs");
                if (segment.Confidence is < 0 or > 1)
                    errors.Add($"{segmentPrefix}.confidence: must be within [0, 1]");

                previousEnd = Math.Max(previousEnd, segment.CharEnd);
            }
        }

        return errors;
    }
}
