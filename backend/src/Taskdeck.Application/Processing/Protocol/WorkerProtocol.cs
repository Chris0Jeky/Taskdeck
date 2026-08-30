using System.Text.Json;
using System.Text.Json.Serialization;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Processing;

namespace Taskdeck.Application.Processing.Protocol;

/// <summary>
/// Taskdeck Worker Protocol <b>v1-alpha</b> (ADR-0065 §Decision 10; spec in
/// <c>docs/architecture/WORKER_PROTOCOL_V1.md</c>): JSON-RPC 2.0 envelopes exchanged between the
/// API and a supervised sidecar over stdio, or mapped onto a queue for hosted workers. This file is
/// the typed contract; the host, supervisor and conformance suite are CF-04 <c>#2258</c>.
/// <para>
/// <b>Stability.</b> The protocol is a draft until two materially different processors pass the
/// CF-04 conformance suite — PdfPig through the memory-contained worker (<c>#1429</c>) and WhisperX
/// through the sidecar path (CF-14). Until then field additions are expected; the wire version
/// stays <c>1</c> and hosts tolerate unknown members.
/// </para>
/// <para>
/// <b>Shape (amended 2026-08-30 after the external audit).</b> A run takes a list of typed
/// <see cref="ProcessorRunInput"/> references (source assets, representations, a bounded context
/// snapshot) rather than one asset, and returns a list of typed <see cref="ProcessorOutput"/>
/// families — <see cref="ProcessorRepresentationOutput"/>, <see cref="ProcessorCandidateBatchOutput"/>,
/// <see cref="ProcessorDiagnosticOutput"/> — so <c>semantic.extract</c> can return candidates,
/// OCR can return regions, and structured extraction can return objects without pretending
/// everything is text. Options common to every capability are typed; capability-specific options
/// travel as an object validated against the manifest's per-capability <c>optionsSchema</c>.
/// </para>
/// </summary>
public static class WorkerProtocol
{
    public const int Version = 1;

    /// <summary>Human-readable stability marker; not a wire field.</summary>
    public const string Stability = "v1-alpha";

    public const string JsonRpcVersion = "2.0";

    public const string RunMethod = "processor.run";
    public const string ProgressMethod = "processor.progress";
    public const string CancelMethod = "processor.cancel";
    public const string DescribeMethod = "processor.describe";

    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
    public const string StatusCancelled = "cancelled";

    public const string InputSourceAsset = "source-asset";
    public const string InputRepresentation = "representation";
    public const string InputContextSnapshot = "context-snapshot";

    public const string OutputRepresentation = "representation";
    public const string OutputCandidateBatch = "candidate-batch";
    public const string OutputDiagnostic = "diagnostic";

    public const string DerivationExtractive = "extractive";
    public const string DerivationInferred = "inferred";

    public const string SeverityInfo = "info";
    public const string SeverityWarning = "warning";
    public const string SeverityError = "error";

    /// <summary>Content-handle schemes the host issues; anything else is rejected before a run starts.</summary>
    public const string SpoolHandleScheme = "spool://";
    public const string ContentHandleScheme = "content://";

    /// <summary>
    /// Wire settings: camelCase, nulls omitted, unknown members tolerated (a newer sidecar may add
    /// fields without breaking an older host). Output families are dispatched on their <c>type</c>
    /// member by <see cref="ProcessorOutputJsonConverter"/>, in any member order.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ProcessorOutputJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);
}

/// <summary>JSON-RPC error codes reserved by the protocol (-32000..-32099 is the server-defined range).</summary>
public static class WorkerProtocolErrorCodes
{
    public const int ProcessorFailure = -32000;
    public const int UnsupportedCapability = -32010;
    public const int UnsupportedMediaType = -32011;
    public const int UnsupportedInput = -32012;
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

    /// <summary>
    /// True only for a well-formed success: a result and no error. A response with neither member is
    /// neither a success nor an error — <see cref="WorkerProtocolValidator.ValidateResponseEnvelope{TResult}"/>
    /// rejects it before a host may act on it.
    /// </summary>
    public bool IsSuccess => Result is not null && Error is null;
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
    IReadOnlyList<ProcessorRunInput>? Inputs,
    ProcessorRunOptions? Options,
    ProcessorRunLimits? Limits);

/// <summary>
/// One typed input reference. <c>Kind</c> is <c>source-asset</c>, <c>representation</c> or
/// <c>context-snapshot</c>. Content reaches a sidecar only through a Taskdeck-managed spool handle
/// (<c>spool://…</c>) or a short-lived authenticated content handle (<c>content://…</c>) — never an
/// arbitrary path the user supplied. A source asset or representation input carries its media type,
/// digest and size so the processor can refuse before reading; a context snapshot is a bounded,
/// host-prepared projection of domain state (aliases, recent targets) and carries only a handle.
/// <c>Role</c> names the input's part in the capability when the capability takes several
/// (<c>audio</c> and <c>transcript</c> for <c>audio.align</c>).
/// </summary>
public sealed record ProcessorRunInput(
    string Kind,
    Guid Id,
    string? MediaType,
    string? ContentHandle,
    string? Sha256,
    long? ByteSize,
    string? Role);

/// <summary>
/// Options every capability understands, plus a capability-specific object (<c>capability</c>) the
/// host validates against the manifest's <c>capabilityContracts[capability].optionsSchema</c>
/// (CF-04). Speech settings such as diarisation or a speaker cap live there, not on the envelope.
/// </summary>
public sealed record ProcessorRunOptions(
    string? Language,
    string? QualityTier,
    JsonElement? Capability);

public sealed record ProcessorRunLimits(
    DateTimeOffset? DeadlineUtc,
    int? MaxWallTimeMs,
    long? MaxOutputBytes);

public sealed record ProcessorCancelParams(string JobId);

public sealed record ProcessorProgressParams(
    string JobId,
    string Phase,
    double? Fraction,
    string? MessageCode);

public sealed record ProcessorRunResult(
    string Status,
    ProcessorIdentity? Processor,
    IReadOnlyList<ProcessorOutput>? Outputs,
    IReadOnlyList<string>? Warnings,
    ProcessorUsage? Usage);

public sealed record ProcessorIdentity(
    string Id,
    string Version,
    string? Model,
    string? ConfigurationHash);

/// <summary>
/// Base of the output families. <c>Type</c> is the discriminator on the wire
/// (<c>representation</c> | <c>candidate-batch</c> | <c>diagnostic</c>).
/// </summary>
public abstract record ProcessorOutput(string Type);

/// <summary>
/// A derived view of an input. The payload is typed by <c>Kind</c>: text kinds
/// (<c>NormalizedText</c>, <c>Transcript</c>, <c>OcrText</c>, <c>ImageDescription</c>) carry
/// <c>Text</c> with optional <c>Segments</c> (char/time) and <c>Regions</c> (page/image geometry);
/// structured kinds (<c>DocumentStructure</c>, <c>StructuredEvent</c>) carry <c>Structured</c>.
/// </summary>
public sealed record ProcessorRepresentationOutput(
    string Kind,
    int SchemaVersion,
    string? Language,
    string? Text,
    IReadOnlyList<ProcessorSegmentOutput>? Segments,
    IReadOnlyList<ProcessorRegionOutput>? Regions,
    JsonElement? Structured) : ProcessorOutput(WorkerProtocol.OutputRepresentation);

/// <summary>Semantic candidates (ADR-0065 §Decision 5) — the typed result of <c>semantic.extract</c>. Never a mutation.</summary>
public sealed record ProcessorCandidateBatchOutput(
    int SchemaVersion,
    IReadOnlyList<ProcessorCandidateOutput>? Candidates) : ProcessorOutput(WorkerProtocol.OutputCandidateBatch);

/// <summary>A content-free finding about the run or its inputs (a missing alignment model, a low-confidence page).</summary>
public sealed record ProcessorDiagnosticOutput(
    string Code,
    string Severity,
    string? SafeDetail) : ProcessorOutput(WorkerProtocol.OutputDiagnostic);

public sealed record ProcessorSegmentOutput(
    int CharStart,
    int CharEnd,
    long? StartMs,
    long? EndMs,
    string? SpeakerLabel,
    double? Confidence);

/// <summary>
/// A normalised rectangle (<c>0..1</c> of the page or image, origin top-left) with an optional page
/// number (documents) and the char range of <c>Text</c> it covers (OCR lines, layout blocks).
/// </summary>
public sealed record ProcessorRegionOutput(
    int? PageNumber,
    double X,
    double Y,
    double Width,
    double Height,
    int? CharStart,
    int? CharEnd,
    double? Confidence);

public sealed record ProcessorCandidateOutput(
    string Kind,
    string Statement,
    JsonElement? Fields,
    IReadOnlyList<ProcessorEvidenceReference>? Evidence,
    string Derivation,
    double? Confidence);

/// <summary>
/// Where a candidate's statement (or one of its fields) comes from: an input representation
/// (<c>RepresentationId</c>) or a representation emitted in the same result (<c>OutputIndex</c>),
/// exactly one of the two, plus the anchor in that representation (<c>AnchorKind</c> is an
/// <c>EvidenceAnchorKind</c> name and fixes which location fields must be present).
/// </summary>
public sealed record ProcessorEvidenceReference(
    Guid? RepresentationId,
    int? OutputIndex,
    string AnchorKind,
    string? FieldName,
    int? CharStart,
    int? CharEnd,
    long? StartMs,
    long? EndMs,
    int? PageNumber,
    ProcessorRegionOutput? Region,
    string? JsonPointer);

/// <summary>
/// Resource and billing usage. Every field is optional; a processor reports what it can measure.
/// <c>BillableUnits</c> + <c>BillableUnitKind</c> (<c>minute</c>, <c>page</c>, <c>token</c>, …)
/// carry the provider's own unit; <c>EstimatedCost</c> is the processor's estimate in
/// <c>Currency</c> and is recorded, never trusted as the invoice.
/// </summary>
public sealed record ProcessorUsage(
    long? WallTimeMs,
    long? AudioDurationMs,
    long? InputTokens,
    long? OutputTokens,
    int? PagesProcessed,
    long? BytesProcessed,
    decimal? BillableUnits,
    string? BillableUnitKind,
    decimal? EstimatedCost,
    string? Currency,
    int? PeakRamMb,
    int? PeakVramMb);

/// <summary>
/// Dispatches <see cref="ProcessorOutput"/> on its <c>type</c> member regardless of member order
/// (System.Text.Json's built-in polymorphism requires the discriminator first, which an untrusted
/// sidecar cannot be relied on to honour). An unknown or missing type becomes a
/// <see cref="ProcessorUnknownOutput"/> so the validator can report it instead of the host throwing.
/// </summary>
public sealed class ProcessorOutputJsonConverter : JsonConverter<ProcessorOutput>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ProcessorOutput);

    public override ProcessorOutput? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new ProcessorUnknownOutput(null);
        }

        string? type = null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                type = property.Value.GetString();
                break;
            }
        }

        var json = element.GetRawText();
        return type switch
        {
            WorkerProtocol.OutputRepresentation => JsonSerializer.Deserialize<ProcessorRepresentationOutput>(json, options),
            WorkerProtocol.OutputCandidateBatch => JsonSerializer.Deserialize<ProcessorCandidateBatchOutput>(json, options),
            WorkerProtocol.OutputDiagnostic => JsonSerializer.Deserialize<ProcessorDiagnosticOutput>(json, options),
            _ => new ProcessorUnknownOutput(type)
        };
    }

    public override void Write(Utf8JsonWriter writer, ProcessorOutput value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

/// <summary>An output whose <c>type</c> the host does not know; reported by the validator, never acted on.</summary>
public sealed record ProcessorUnknownOutput(string? DeclaredType) : ProcessorOutput(DeclaredType ?? string.Empty);

/// <summary>
/// Structural validation of protocol messages: the JSON-RPC envelopes, run parameters and results.
/// Host-side enforcement of deadlines, output caps, cancellation grace and network denial belongs to
/// the supervisor (CF-04); this validator only rejects malformed messages before any work starts or
/// any result is persisted. Every enum-valued string is matched against <c>Enum.GetNames</c>, never
/// <c>Enum.TryParse</c>, so a numeric string cannot pass as a name.
/// </summary>
public static class WorkerProtocolValidator
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        WorkerProtocol.StatusCompleted, WorkerProtocol.StatusFailed, WorkerProtocol.StatusCancelled
    };

    private static readonly HashSet<string> InputKinds = new(StringComparer.Ordinal)
    {
        WorkerProtocol.InputSourceAsset, WorkerProtocol.InputRepresentation, WorkerProtocol.InputContextSnapshot
    };

    private static readonly HashSet<string> Derivations = new(StringComparer.Ordinal)
    {
        WorkerProtocol.DerivationExtractive, WorkerProtocol.DerivationInferred
    };

    private static readonly HashSet<string> Severities = new(StringComparer.Ordinal)
    {
        WorkerProtocol.SeverityInfo, WorkerProtocol.SeverityWarning, WorkerProtocol.SeverityError
    };

    private static readonly HashSet<string> RepresentationKinds = new(Enum.GetNames<RepresentationKind>(), StringComparer.Ordinal);
    private static readonly HashSet<string> CandidateKinds = new(Enum.GetNames<SemanticCandidateKind>(), StringComparer.Ordinal);
    private static readonly HashSet<string> AnchorKinds = new(Enum.GetNames<EvidenceAnchorKind>(), StringComparer.Ordinal);

    private static readonly HashSet<string> TextRepresentationKinds = new(StringComparer.Ordinal)
    {
        nameof(RepresentationKind.NormalizedText),
        nameof(RepresentationKind.Transcript),
        nameof(RepresentationKind.OcrText),
        nameof(RepresentationKind.ImageDescription)
    };

    private const double RectangleTolerance = 1e-9;

    /// <summary>
    /// JSON-RPC 2.0 response rules: the version string, a non-empty id (the host matches it to the
    /// request), and exactly one of <c>result</c> / <c>error</c>.
    /// </summary>
    public static IReadOnlyList<string> ValidateResponseEnvelope<TResult>(JsonRpcResponse<TResult>? response, string? expectedId = null)
    {
        var errors = new List<string>();

        if (response is null)
        {
            errors.Add("response: missing");
            return errors;
        }

        if (!string.Equals(response.JsonRpc, WorkerProtocol.JsonRpcVersion, StringComparison.Ordinal))
            errors.Add($"response.jsonrpc: must be \"{WorkerProtocol.JsonRpcVersion}\"");

        if (string.IsNullOrWhiteSpace(response.Id))
            errors.Add("response.id: required");
        else if (expectedId is not null && !string.Equals(response.Id, expectedId, StringComparison.Ordinal))
            errors.Add($"response.id: expected '{expectedId}'");

        var hasResult = response.Result is not null;
        var hasError = response.Error is not null;
        if (hasResult == hasError)
            errors.Add("response: exactly one of result or error must be present");

        if (hasError && string.IsNullOrWhiteSpace(response.Error!.Message))
            errors.Add("response.error.message: required");

        return errors;
    }

    /// <summary>
    /// JSON-RPC 2.0 notification rules: the version string, exactly the expected method (spelling
    /// is exact — <c>processor.progress</c>, never a variant), and a params object.
    /// </summary>
    public static IReadOnlyList<string> ValidateNotificationEnvelope<TParams>(JsonRpcNotification<TParams>? notification, string expectedMethod)
    {
        var errors = new List<string>();

        if (notification is null)
        {
            errors.Add("notification: missing");
            return errors;
        }

        if (!string.Equals(notification.JsonRpc, WorkerProtocol.JsonRpcVersion, StringComparison.Ordinal))
            errors.Add($"notification.jsonrpc: must be \"{WorkerProtocol.JsonRpcVersion}\"");

        if (!string.Equals(notification.Method, expectedMethod, StringComparison.Ordinal))
            errors.Add($"notification.method: must be '{expectedMethod}'");

        if (notification.Params is null)
            errors.Add("notification.params: required");

        return errors;
    }

    /// <summary>Progress notifications must be correlatable and bounded before any state consumes them.</summary>
    public static IReadOnlyList<string> ValidateProgress(ProcessorProgressParams? progress)
    {
        var errors = new List<string>();

        if (progress is null)
        {
            errors.Add("progress: missing");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(progress.JobId))
            errors.Add("progress.jobId: required");
        if (string.IsNullOrWhiteSpace(progress.Phase))
            errors.Add("progress.phase: required");
        if (progress.Fraction is < 0 or > 1)
            errors.Add("progress.fraction: must be within [0, 1]");
        if (progress.Fraction is double fraction && double.IsNaN(fraction))
            errors.Add("progress.fraction: must be a number");

        return errors;
    }

    public static IReadOnlyList<string> ValidateCancel(ProcessorCancelParams? cancel)
    {
        var errors = new List<string>();

        if (cancel is null)
        {
            errors.Add("cancel: missing");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(cancel.JobId))
            errors.Add("cancel.jobId: required");

        return errors;
    }

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

        if (parameters.Inputs is null || parameters.Inputs.Count == 0)
        {
            errors.Add("params.inputs: at least one input is required");
        }
        else
        {
            var seen = new HashSet<Guid>();
            for (var index = 0; index < parameters.Inputs.Count; index++)
            {
                var input = parameters.Inputs[index];
                var prefix = $"params.inputs[{index}]";

                if (input is null)
                {
                    errors.Add($"{prefix}: must not be null");
                    continue;
                }

                ValidateInput(input, prefix, errors);

                if (input.Id != Guid.Empty && !seen.Add(input.Id))
                    errors.Add($"{prefix}.id: '{input.Id}' is referenced twice");
            }
        }

        if (parameters.Options is { Capability: { } capabilityOptions } && capabilityOptions.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null))
            errors.Add("params.options.capability: must be an object");

        if (parameters.Limits is { MaxWallTimeMs: <= 0 })
            errors.Add("params.limits.maxWallTimeMs: must be greater than zero");

        if (parameters.Limits is { MaxOutputBytes: <= 0 })
            errors.Add("params.limits.maxOutputBytes: must be greater than zero");

        return errors;
    }

    private static void ValidateInput(ProcessorRunInput input, string prefix, List<string> errors)
    {
        if (input.Kind is null || !InputKinds.Contains(input.Kind))
            errors.Add($"{prefix}.kind: one of {WorkerProtocol.InputSourceAsset} | {WorkerProtocol.InputRepresentation} | {WorkerProtocol.InputContextSnapshot}");

        if (input.Id == Guid.Empty)
            errors.Add($"{prefix}.id: required");

        if (string.IsNullOrWhiteSpace(input.ContentHandle))
            errors.Add($"{prefix}.contentHandle: required");
        else if (!input.ContentHandle.StartsWith(WorkerProtocol.SpoolHandleScheme, StringComparison.Ordinal)
                 && !input.ContentHandle.StartsWith(WorkerProtocol.ContentHandleScheme, StringComparison.Ordinal))
            errors.Add($"{prefix}.contentHandle: must be a host-issued {WorkerProtocol.SpoolHandleScheme} or {WorkerProtocol.ContentHandleScheme} handle, never a path");

        var contentBearing = input.Kind is WorkerProtocol.InputSourceAsset or WorkerProtocol.InputRepresentation;
        if (!contentBearing)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(input.MediaType))
            errors.Add($"{prefix}.mediaType: required for a {input.Kind} input");
        if (input.Sha256 is null || input.Sha256.Length != 64 || input.Sha256.Any(character => !Uri.IsHexDigit(character)))
            errors.Add($"{prefix}.sha256: must be a 64-character hexadecimal digest");
        if (input.ByteSize is null or <= 0)
            errors.Add($"{prefix}.byteSize: must be greater than zero");
    }

    /// <summary>
    /// Structural validation of a result on its own. Prefer the overload that takes the run request:
    /// only with the request can the host reject a candidate batch from a capability that may not
    /// emit one, or an evidence reference to a representation that was not an input of this run.
    /// </summary>
    public static IReadOnlyList<string> ValidateResult(ProcessorRunResult? result) => ValidateResult(result, request: null);

    /// <summary>
    /// Validates a result <b>against the run that produced it</b>. With <paramref name="request"/>
    /// present, a <c>candidate-batch</c> output is accepted only from a <c>semantic.extract</c> run,
    /// and a candidate's <c>representationId</c> must name one of the run's <c>representation</c>
    /// inputs — a processor can neither inject candidates outside the capability it was authorised
    /// for nor claim lineage to a representation it was never given (Codex review of PR #2320).
    /// Ownership of those inputs is the host's (they were issued by it); the manifest contract check
    /// (which output families the capability declared) is applied by the CF-04 host with the manifest.
    /// </summary>
    public static IReadOnlyList<string> ValidateResult(ProcessorRunResult? result, ProcessorRunParams? request)
    {
        var errors = new List<string>();

        if (result is null)
        {
            errors.Add("result: missing");
            return errors;
        }

        var runContext = request is null ? null : new RunContext(request);

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
            if (result.Status == WorkerProtocol.StatusCompleted && string.IsNullOrWhiteSpace(result.Processor.ConfigurationHash))
                errors.Add("result.processor.configurationHash: required on a completed run (provenance and cache identity)");
        }

        ValidateUsage(result.Usage, errors);

        if (result.Warnings is not null)
        {
            for (var index = 0; index < result.Warnings.Count; index++)
            {
                if (result.Warnings[index] is null)
                    errors.Add($"result.warnings[{index}]: must not be null");
            }
        }

        var hasUsableOutput = result.Outputs?.Any(output => output is ProcessorRepresentationOutput or ProcessorCandidateBatchOutput) == true;
        if (result.Status == WorkerProtocol.StatusCompleted && !hasUsableOutput)
            errors.Add("result.outputs: a completed run must emit at least one representation or candidate batch");

        if (result.Outputs is null)
            return errors;

        for (var index = 0; index < result.Outputs.Count; index++)
        {
            var output = result.Outputs[index];
            var prefix = $"result.outputs[{index}]";

            switch (output)
            {
                case null:
                    // System.Text.Json admits a null array element regardless of the annotation; an
                    // untrusted sidecar must not be able to turn that into a host exception.
                    errors.Add($"{prefix}: must not be null");
                    break;
                case ProcessorRepresentationOutput representation:
                    ValidateRepresentation(representation, prefix, errors);
                    break;
                case ProcessorCandidateBatchOutput batch:
                    if (runContext is not null && runContext.Capability != ProcessingCapability.SemanticExtract)
                        errors.Add($"{prefix}: a candidate batch may only be emitted by {ProcessingCapability.SemanticExtract}; this run is '{runContext.Capability}'");
                    ValidateCandidateBatch(batch, prefix, result.Outputs, runContext, errors);
                    break;
                case ProcessorDiagnosticOutput diagnostic:
                    if (string.IsNullOrWhiteSpace(diagnostic.Code))
                        errors.Add($"{prefix}.code: required");
                    if (diagnostic.Severity is null || !Severities.Contains(diagnostic.Severity))
                        errors.Add($"{prefix}.severity: one of info | warning | error");
                    break;
                default:
                    errors.Add($"{prefix}.type: '{output.Type}' is not one of {WorkerProtocol.OutputRepresentation} | {WorkerProtocol.OutputCandidateBatch} | {WorkerProtocol.OutputDiagnostic}");
                    break;
            }
        }

        return errors;
    }

    private static void ValidateUsage(ProcessorUsage? usage, List<string> errors)
    {
        if (usage is null)
            return;

        void NonNegative(long? value, string field)
        {
            if (value is < 0)
                errors.Add($"result.usage.{field}: cannot be negative");
        }

        NonNegative(usage.WallTimeMs, "wallTimeMs");
        NonNegative(usage.AudioDurationMs, "audioDurationMs");
        NonNegative(usage.InputTokens, "inputTokens");
        NonNegative(usage.OutputTokens, "outputTokens");
        NonNegative(usage.PagesProcessed, "pagesProcessed");
        NonNegative(usage.BytesProcessed, "bytesProcessed");
        NonNegative(usage.PeakRamMb, "peakRamMb");
        NonNegative(usage.PeakVramMb, "peakVramMb");

        if (usage.BillableUnits is < 0)
            errors.Add("result.usage.billableUnits: cannot be negative");
        if (usage.EstimatedCost is < 0)
            errors.Add("result.usage.estimatedCost: cannot be negative");
        if (usage.BillableUnits is not null && string.IsNullOrWhiteSpace(usage.BillableUnitKind))
            errors.Add("result.usage.billableUnitKind: required when billableUnits is reported");
        if (usage.EstimatedCost is not null && (usage.Currency is null || usage.Currency.Length != 3 || usage.Currency.Any(character => character is < 'A' or > 'Z')))
            errors.Add("result.usage.currency: a three-letter ISO code is required when estimatedCost is reported");
    }

    private static void ValidateRepresentation(ProcessorRepresentationOutput representation, string prefix, List<string> errors)
    {
        var kindKnown = representation.Kind is not null && RepresentationKinds.Contains(representation.Kind);
        if (string.IsNullOrWhiteSpace(representation.Kind))
            errors.Add($"{prefix}.kind: required");
        else if (!kindKnown)
            errors.Add($"{prefix}.kind: '{representation.Kind}' is not a RepresentationKind");

        if (representation.SchemaVersion < 1)
            errors.Add($"{prefix}.schemaVersion: must be at least 1");

        var hasStructured = representation.Structured is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) };
        if (kindKnown)
        {
            if (TextRepresentationKinds.Contains(representation.Kind!))
            {
                if (representation.Text is null)
                    errors.Add($"{prefix}.text: required for a {representation.Kind} representation (may be empty, never absent)");
            }
            else
            {
                if (!hasStructured || representation.Structured!.Value.ValueKind != JsonValueKind.Object)
                    errors.Add($"{prefix}.structured: an object is required for a {representation.Kind} representation");
                if (representation.Segments is { Count: > 0 })
                    errors.Add($"{prefix}.segments: not permitted on a {representation.Kind} representation");
            }
        }

        var textLength = representation.Text?.Length ?? 0;

        if (representation.Segments is not null)
        {
            if (representation.Text is null)
                errors.Add($"{prefix}.segments: require text");

            var previousEnd = 0;
            for (var segmentIndex = 0; segmentIndex < representation.Segments.Count; segmentIndex++)
            {
                var segment = representation.Segments[segmentIndex];
                var segmentPrefix = $"{prefix}.segments[{segmentIndex}]";

                if (segment is null)
                {
                    errors.Add($"{segmentPrefix}: must not be null");
                    continue;
                }

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

        if (representation.Regions is null)
            return;

        for (var regionIndex = 0; regionIndex < representation.Regions.Count; regionIndex++)
        {
            var region = representation.Regions[regionIndex];
            var regionPrefix = $"{prefix}.regions[{regionIndex}]";

            if (region is null)
            {
                errors.Add($"{regionPrefix}: must not be null");
                continue;
            }

            ValidateRegion(region, regionPrefix, textLength, representation.Text is not null, errors);
        }
    }

    private static void ValidateRegion(ProcessorRegionOutput region, string prefix, int textLength, bool hasText, List<string> errors)
    {
        if (region.PageNumber is < 1)
            errors.Add($"{prefix}.pageNumber: must be at least 1");

        var coordinates = new[] { region.X, region.Y, region.Width, region.Height };
        if (coordinates.Any(double.IsNaN) || region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || region.X + region.Width > 1 + RectangleTolerance || region.Y + region.Height > 1 + RectangleTolerance)
            errors.Add($"{prefix}: rectangle must be normalised (0 <= x, y; width, height > 0; x + width <= 1; y + height <= 1)");

        if (region.CharStart is not null || region.CharEnd is not null)
        {
            if (!hasText)
                errors.Add($"{prefix}: a char range requires text");
            else if (region.CharStart is null || region.CharEnd is null || region.CharStart < 0 || region.CharEnd < region.CharStart || region.CharEnd > textLength)
                errors.Add($"{prefix}: char range must satisfy 0 <= charStart <= charEnd <= text.length");
        }

        if (region.Confidence is < 0 or > 1)
            errors.Add($"{prefix}.confidence: must be within [0, 1]");
    }

    /// <summary>What the host knows about the run a result claims to answer: its capability and the representation inputs it was issued.</summary>
    private sealed class RunContext
    {
        public RunContext(ProcessorRunParams request)
        {
            Capability = request.Capability ?? string.Empty;
            RepresentationInputIds = new HashSet<Guid>(
                (request.Inputs ?? Array.Empty<ProcessorRunInput>())
                    .Where(input => input is { Kind: WorkerProtocol.InputRepresentation } && input.Id != Guid.Empty)
                    .Select(input => input.Id));
        }

        public string Capability { get; }
        public HashSet<Guid> RepresentationInputIds { get; }
    }

    private static void ValidateCandidateBatch(
        ProcessorCandidateBatchOutput batch,
        string prefix,
        IReadOnlyList<ProcessorOutput> outputs,
        RunContext? runContext,
        List<string> errors)
    {
        if (batch.SchemaVersion < 1)
            errors.Add($"{prefix}.schemaVersion: must be at least 1");

        if (batch.Candidates is null)
        {
            errors.Add($"{prefix}.candidates: required (may be empty, never absent)");
            return;
        }

        for (var candidateIndex = 0; candidateIndex < batch.Candidates.Count; candidateIndex++)
        {
            var candidate = batch.Candidates[candidateIndex];
            var candidatePrefix = $"{prefix}.candidates[{candidateIndex}]";

            if (candidate is null)
            {
                errors.Add($"{candidatePrefix}: must not be null");
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.Kind))
                errors.Add($"{candidatePrefix}.kind: required");
            else if (!CandidateKinds.Contains(candidate.Kind))
                errors.Add($"{candidatePrefix}.kind: '{candidate.Kind}' is not a SemanticCandidateKind");

            if (string.IsNullOrWhiteSpace(candidate.Statement))
                errors.Add($"{candidatePrefix}.statement: required");

            if (candidate.Derivation is null || !Derivations.Contains(candidate.Derivation))
                errors.Add($"{candidatePrefix}.derivation: one of extractive | inferred");

            if (candidate.Confidence is < 0 or > 1)
                errors.Add($"{candidatePrefix}.confidence: must be within [0, 1]");

            if (candidate.Fields is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null) })
                errors.Add($"{candidatePrefix}.fields: must be an object");

            if (candidate.Derivation == WorkerProtocol.DerivationExtractive && (candidate.Evidence is null || candidate.Evidence.Count == 0))
                errors.Add($"{candidatePrefix}.evidence: an extractive candidate must cite at least one anchor");

            if (candidate.Evidence is null)
                continue;

            for (var evidenceIndex = 0; evidenceIndex < candidate.Evidence.Count; evidenceIndex++)
            {
                var evidence = candidate.Evidence[evidenceIndex];
                var evidencePrefix = $"{candidatePrefix}.evidence[{evidenceIndex}]";

                if (evidence is null)
                {
                    errors.Add($"{evidencePrefix}: must not be null");
                    continue;
                }

                ValidateEvidence(evidence, evidencePrefix, outputs, runContext, errors);
            }
        }
    }

    private static void ValidateEvidence(
        ProcessorEvidenceReference evidence,
        string prefix,
        IReadOnlyList<ProcessorOutput> outputs,
        RunContext? runContext,
        List<string> errors)
    {
        var hasRepresentation = evidence.RepresentationId is { } representationId && representationId != Guid.Empty;
        var hasOutputIndex = evidence.OutputIndex is not null;
        if (hasRepresentation == hasOutputIndex)
            errors.Add($"{prefix}: exactly one of representationId or outputIndex must be present");

        if (hasRepresentation && runContext is not null && !runContext.RepresentationInputIds.Contains(evidence.RepresentationId!.Value))
            errors.Add($"{prefix}.representationId: must reference one of this run's representation inputs");

        string? anchoredText = null;
        if (hasOutputIndex)
        {
            var outputIndex = evidence.OutputIndex!.Value;
            if (outputIndex < 0 || outputIndex >= outputs.Count || outputs[outputIndex] is not ProcessorRepresentationOutput target)
                errors.Add($"{prefix}.outputIndex: must reference a representation output in this result");
            else
                anchoredText = target.Text;
        }

        if (string.IsNullOrWhiteSpace(evidence.AnchorKind))
        {
            errors.Add($"{prefix}.anchorKind: required");
            return;
        }

        if (!AnchorKinds.Contains(evidence.AnchorKind))
        {
            errors.Add($"{prefix}.anchorKind: '{evidence.AnchorKind}' is not an EvidenceAnchorKind");
            return;
        }

        switch (Enum.Parse<EvidenceAnchorKind>(evidence.AnchorKind))
        {
            case EvidenceAnchorKind.TextSpan:
                if (evidence.CharStart is null || evidence.CharEnd is null || evidence.CharStart < 0 || evidence.CharEnd < evidence.CharStart)
                    errors.Add($"{prefix}: a TextSpan anchor needs 0 <= charStart <= charEnd");
                else if (anchoredText is not null && evidence.CharEnd > anchoredText.Length)
                    errors.Add($"{prefix}: a TextSpan anchor cannot extend past the referenced text");
                break;
            case EvidenceAnchorKind.TimeRange:
                if (evidence.StartMs is null || evidence.EndMs is null || evidence.StartMs < 0 || evidence.EndMs < evidence.StartMs)
                    errors.Add($"{prefix}: a TimeRange anchor needs 0 <= startMs <= endMs");
                break;
            case EvidenceAnchorKind.PageRegion:
                if (evidence.PageNumber is null or < 1 || evidence.Region is null)
                    errors.Add($"{prefix}: a PageRegion anchor needs a pageNumber and a region");
                else
                    ValidateRegion(evidence.Region, $"{prefix}.region", anchoredText?.Length ?? 0, anchoredText is not null, errors);
                break;
            case EvidenceAnchorKind.ImageRegion:
                if (evidence.Region is null)
                    errors.Add($"{prefix}: an ImageRegion anchor needs a region");
                else
                    ValidateRegion(evidence.Region, $"{prefix}.region", anchoredText?.Length ?? 0, anchoredText is not null, errors);
                break;
            case EvidenceAnchorKind.JsonPointer:
                if (string.IsNullOrEmpty(evidence.JsonPointer) || !evidence.JsonPointer.StartsWith('/'))
                    errors.Add($"{prefix}: a JsonPointer anchor needs a pointer starting with '/'");
                break;
            case EvidenceAnchorKind.WholeSource:
                break;
        }
    }
}
