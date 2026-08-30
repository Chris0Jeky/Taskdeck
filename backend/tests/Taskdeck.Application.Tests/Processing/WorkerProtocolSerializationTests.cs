using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.Processing.Protocol;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Processing;
using Xunit;

namespace Taskdeck.Application.Tests.Processing;

public sealed class WorkerProtocolSerializationTests
{
    private const string ValidSha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    /// <summary>The proof-of-concept job request from the 2026-08-30 pack, in the v1-alpha shape (typed inputs, capability options).</summary>
    private const string PocRequest = """
        {
          "jsonrpc": "2.0",
          "id": "job-7f41",
          "method": "processor.run",
          "params": {
            "protocolVersion": 1,
            "capability": "audio.transcribe",
            "inputs": [
              {
                "kind": "source-asset",
                "id": "7ec00b5e-7b9f-4ebd-8a31-f018340bb0aa",
                "mediaType": "audio/webm",
                "contentHandle": "spool://2b9e",
                "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                "byteSize": 2481000,
                "role": "audio"
              }
            ],
            "options": {
              "language": "auto",
              "qualityTier": "balanced",
              "capability": { "wordTimestamps": false, "diarization": false }
            },
            "limits": {
              "deadlineUtc": "2026-08-30T03:00:00Z",
              "maxWallTimeMs": 600000,
              "maxOutputBytes": 5000000
            }
          }
        }
        """;

    private const string PocProgress = """
        {
          "jsonrpc": "2.0",
          "method": "processor.progress",
          "params": {
            "jobId": "job-7f41",
            "phase": "transcribing",
            "fraction": 0.54,
            "messageCode": "audio.transcribing"
          }
        }
        """;

    private const string PocResult = """
        {
          "jsonrpc": "2.0",
          "id": "job-7f41",
          "result": {
            "status": "completed",
            "processor": {
              "id": "taskdeck.whisperx",
              "version": "1.0.0",
              "model": "large-v3-turbo",
              "configurationHash": "sha256:abc"
            },
            "outputs": [
              {
                "type": "representation",
                "kind": "Transcript",
                "schemaVersion": 1,
                "language": "en",
                "text": "We need the image intake spike finished next week. Ask Cosmin to review the UX.",
                "segments": [
                  {
                    "charStart": 0,
                    "charEnd": 50,
                    "startMs": 4200,
                    "endMs": 11840,
                    "speakerLabel": "SPEAKER_00",
                    "confidence": 0.93
                  },
                  {
                    "charStart": 51,
                    "charEnd": 79,
                    "startMs": 11900,
                    "endMs": 14100,
                    "speakerLabel": "SPEAKER_00",
                    "confidence": 0.9
                  }
                ]
              },
              {
                "type": "diagnostic",
                "code": "ALIGNMENT_MODEL_MISSING",
                "severity": "warning",
                "safeDetail": "No alignment model for language 'en-XX'; segment timestamps only."
              }
            ],
            "warnings": [],
            "usage": {
              "wallTimeMs": 18420,
              "audioDurationMs": 181000,
              "billableUnits": 3.02,
              "billableUnitKind": "minute",
              "peakRamMb": 3210,
              "peakVramMb": 5740
            }
          }
        }
        """;

    private const string PocFailure = """
        {
          "jsonrpc": "2.0",
          "id": "job-7f41",
          "error": {
            "code": -32020,
            "message": "Processor could not allocate the requested model",
            "data": {
              "errorCode": "RESOURCE_EXHAUSTED",
              "retryable": true,
              "safeDetail": "Try a smaller model or CPU fallback."
            }
          }
        }
        """;

    private static ProcessorRunInput SourceAssetInput(string handle = "spool://img", string mediaType = "image/png") =>
        new(WorkerProtocol.InputSourceAsset, Guid.NewGuid(), mediaType, handle, ValidSha, 1234, null);

    private static ProcessorIdentity CompletedIdentity() => new("taskdeck.mock", "1.0.0", null, "sha256:mock");

    private static ProcessorRepresentationOutput TextOutput(string kind = "Transcript", string? text = "short text") =>
        new(kind, 1, "en", text, null, null, null);

    [Fact]
    public void PocRequest_ShouldDeserializeAndValidate()
    {
        var request = WorkerProtocol.Deserialize<JsonRpcRequest<ProcessorRunParams>>(PocRequest);

        request.Should().NotBeNull();
        request!.JsonRpc.Should().Be("2.0");
        request.Id.Should().Be("job-7f41");
        request.Method.Should().Be(WorkerProtocol.RunMethod);
        request.Params.ProtocolVersion.Should().Be(WorkerProtocol.Version);
        request.Params.Capability.Should().Be(ProcessingCapability.AudioTranscribe);
        var input = request.Params.Inputs.Should().ContainSingle().Subject;
        input.Kind.Should().Be(WorkerProtocol.InputSourceAsset);
        input.ContentHandle.Should().Be("spool://2b9e");
        input.Role.Should().Be("audio");
        request.Params.Options!.Capability!.Value.GetProperty("diarization").GetBoolean().Should().BeFalse();
        request.Params.Limits!.DeadlineUtc.Should().Be(new DateTimeOffset(2026, 8, 30, 3, 0, 0, TimeSpan.Zero));

        WorkerProtocolValidator.ValidateRunParams(request.Params).Should().BeEmpty();
    }

    [Fact]
    public void Request_ShouldRoundTripWithTheJsonRpcPropertyName()
    {
        var parameters = new ProcessorRunParams(
            WorkerProtocol.Version,
            ProcessingCapability.ImageOcr,
            new[] { SourceAssetInput() },
            null,
            new ProcessorRunLimits(null, 30_000, null));
        var request = JsonRpcRequest<ProcessorRunParams>.Create("job-1", WorkerProtocol.RunMethod, parameters);

        var json = WorkerProtocol.Serialize(request);
        var back = WorkerProtocol.Deserialize<JsonRpcRequest<ProcessorRunParams>>(json);

        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().NotContain("\"options\"", "null members are omitted on the wire");
        back.Should().BeEquivalentTo(request);
    }

    [Fact]
    public void PocProgress_ShouldDeserializeAndValidateItsEnvelope()
    {
        var notification = WorkerProtocol.Deserialize<JsonRpcNotification<ProcessorProgressParams>>(PocProgress);

        notification.Should().NotBeNull();
        notification!.Method.Should().Be(WorkerProtocol.ProgressMethod);
        notification.Params.Fraction.Should().BeApproximately(0.54, 0.0001);
        notification.Params.MessageCode.Should().Be("audio.transcribing");
        WorkerProtocolValidator.ValidateNotificationEnvelope(notification, WorkerProtocol.ProgressMethod).Should().BeEmpty();
    }

    [Fact]
    public void ValidateNotificationEnvelope_ShouldRequireTheExactMethodVersionAndParams()
    {
        var wrongMethod = new JsonRpcNotification<ProcessorProgressParams>("2.0", "Processor.Progress", new ProcessorProgressParams("job-1", "x", null, null));
        var noParams = new JsonRpcNotification<ProcessorProgressParams>("1.0", WorkerProtocol.ProgressMethod, null!);

        WorkerProtocolValidator.ValidateNotificationEnvelope(wrongMethod, WorkerProtocol.ProgressMethod)
            .Should().ContainSingle(error => error.Contains("must be 'processor.progress'"));
        var errors = WorkerProtocolValidator.ValidateNotificationEnvelope(noParams, WorkerProtocol.ProgressMethod);
        errors.Should().Contain(error => error.Contains("notification.jsonrpc"));
        errors.Should().Contain("notification.params: required");
        WorkerProtocolValidator.ValidateNotificationEnvelope<ProcessorProgressParams>(null, WorkerProtocol.ProgressMethod)
            .Should().ContainSingle(error => error == "notification: missing");
    }

    [Fact]
    public void PocResult_ShouldDeserializeTypedOutputsAndValidate()
    {
        var response = WorkerProtocol.Deserialize<JsonRpcResponse<ProcessorRunResult>>(PocResult);

        response.Should().NotBeNull();
        response!.IsError.Should().BeFalse();
        response.Result!.Status.Should().Be(WorkerProtocol.StatusCompleted);
        response.Result.Processor!.Model.Should().Be("large-v3-turbo");
        response.Result.Outputs.Should().HaveCount(2);
        var representation = response.Result.Outputs![0].Should().BeOfType<ProcessorRepresentationOutput>().Subject;
        representation.Kind.Should().Be(nameof(RepresentationKind.Transcript));
        representation.Segments.Should().HaveCount(2);
        var diagnostic = response.Result.Outputs[1].Should().BeOfType<ProcessorDiagnosticOutput>().Subject;
        diagnostic.Code.Should().Be("ALIGNMENT_MODEL_MISSING");
        response.Result.Usage!.BillableUnits.Should().Be(3.02m);
        response.Result.Usage.PeakVramMb.Should().Be(5740);

        WorkerProtocolValidator.ValidateResult(response.Result).Should().BeEmpty();
    }

    [Fact]
    public void Outputs_ShouldDispatchOnTypeRegardlessOfMemberOrder()
    {
        // The discriminator is the LAST member here; System.Text.Json's built-in polymorphism would
        // reject this, and an untrusted sidecar may well emit it.
        const string json = """
            {
              "status": "completed",
              "processor": { "id": "taskdeck.mock", "version": "1.0.0", "configurationHash": "sha256:mock" },
              "outputs": [
                { "kind": "NormalizedText", "schemaVersion": 1, "text": "hello", "type": "representation" },
                { "code": "NOTE", "severity": "info", "type": "diagnostic" }
              ]
            }
            """;

        var result = WorkerProtocol.Deserialize<ProcessorRunResult>(json);

        result!.Outputs![0].Should().BeOfType<ProcessorRepresentationOutput>();
        result.Outputs[1].Should().BeOfType<ProcessorDiagnosticOutput>();
        WorkerProtocolValidator.ValidateResult(result).Should().BeEmpty();
    }

    [Fact]
    public void Outputs_ShouldRoundTripThroughTheConverter()
    {
        var result = new ProcessorRunResult(
            WorkerProtocol.StatusCompleted,
            CompletedIdentity(),
            new ProcessorOutput[]
            {
                TextOutput(),
                new ProcessorCandidateBatchOutput(1, new[]
                {
                    new ProcessorCandidateOutput(
                        nameof(SemanticCandidateKind.Action), "Finish the spike", null,
                        new[] { new ProcessorEvidenceReference(null, 0, nameof(EvidenceAnchorKind.TextSpan), null, 0, 5, null, null, null, null, null) },
                        WorkerProtocol.DerivationExtractive, 0.8)
                }),
                new ProcessorDiagnosticOutput("LOW_CONFIDENCE_PAGE", WorkerProtocol.SeverityInfo, null)
            },
            null,
            null);

        var json = WorkerProtocol.Serialize(result);
        var back = WorkerProtocol.Deserialize<ProcessorRunResult>(json);

        json.Should().Contain("\"type\":\"candidate-batch\"");
        back!.Outputs.Should().HaveCount(3);
        back.Outputs![1].Should().BeOfType<ProcessorCandidateBatchOutput>();
        back.Outputs[2].Should().BeOfType<ProcessorDiagnosticOutput>();
        WorkerProtocolValidator.ValidateResult(back).Should().BeEmpty();
    }

    [Fact]
    public void UnknownOutputType_ShouldBeReportedNotThrown()
    {
        var json = PocResult.Replace("\"type\": \"diagnostic\"", "\"type\": \"board-mutation\"");

        var response = WorkerProtocol.Deserialize<JsonRpcResponse<ProcessorRunResult>>(json);

        response!.Result!.Outputs![1].Should().BeOfType<ProcessorUnknownOutput>();
        WorkerProtocolValidator.ValidateResult(response.Result)
            .Should().ContainSingle(error => error.Contains("outputs[1].type") && error.Contains("board-mutation"));
    }

    [Fact]
    public void PocFailure_ShouldDeserializeAsAnError()
    {
        var response = WorkerProtocol.Deserialize<JsonRpcResponse<ProcessorRunResult>>(PocFailure);

        response.Should().NotBeNull();
        response!.IsError.Should().BeTrue();
        response.Result.Should().BeNull();
        response.Error!.Code.Should().Be(WorkerProtocolErrorCodes.ResourceExhausted);
        response.Error.Data!.ErrorCode.Should().Be("RESOURCE_EXHAUSTED");
        response.Error.Data.Retryable.Should().BeTrue();
    }

    [Fact]
    public void ValidateRunParams_ShouldRejectWrongVersionUnknownCapabilityAndBadInput()
    {
        var parameters = new ProcessorRunParams(
            2,
            "board.mutate",
            new[] { new ProcessorRunInput("blob", Guid.Empty, "", "", "nope", 0, null) },
            new ProcessorRunOptions(null, null, JsonDocument.Parse("[1]").RootElement),
            new ProcessorRunLimits(null, 0, 0));

        var errors = WorkerProtocolValidator.ValidateRunParams(parameters);

        errors.Should().Contain(error => error.Contains("protocolVersion"));
        errors.Should().Contain(error => error.Contains("not a known capability"));
        errors.Should().Contain(error => error.Contains("inputs[0].kind"));
        errors.Should().Contain(error => error.Contains("inputs[0].id"));
        errors.Should().Contain(error => error.Contains("inputs[0].contentHandle"));
        errors.Should().Contain(error => error.Contains("options.capability: must be an object"));
        errors.Should().Contain(error => error.Contains("maxWallTimeMs"));
        errors.Should().Contain(error => error.Contains("maxOutputBytes"));
    }

    [Fact]
    public void ValidateRunParams_ShouldRequireContentFieldsOnlyForContentBearingInputs()
    {
        var assetWithoutContent = new ProcessorRunInput(WorkerProtocol.InputSourceAsset, Guid.NewGuid(), null, "spool://a", null, null, null);
        var snapshot = new ProcessorRunInput(WorkerProtocol.InputContextSnapshot, Guid.NewGuid(), null, "content://ctx", null, null, "context");

        var assetErrors = WorkerProtocolValidator.ValidateRunParams(
            new ProcessorRunParams(WorkerProtocol.Version, ProcessingCapability.ImageOcr, new[] { assetWithoutContent }, null, null));
        assetErrors.Should().Contain(error => error.Contains("inputs[0].mediaType"));
        assetErrors.Should().Contain(error => error.Contains("inputs[0].sha256"));
        assetErrors.Should().Contain(error => error.Contains("inputs[0].byteSize"));

        WorkerProtocolValidator.ValidateRunParams(
                new ProcessorRunParams(WorkerProtocol.Version, ProcessingCapability.SemanticExtract, new[] { SourceAssetInput(), snapshot }, null, null))
            .Should().BeEmpty("a context snapshot carries only a handle");
    }

    [Fact]
    public void ValidateRunParams_ShouldRejectAContentHandleThatIsNotHostIssued()
    {
        var parameters = new ProcessorRunParams(
            WorkerProtocol.Version,
            ProcessingCapability.AudioTranscribe,
            new[] { SourceAssetInput(@"C:\Users\someone\secret.webm", "audio/webm") },
            null,
            null);

        WorkerProtocolValidator.ValidateRunParams(parameters)
            .Should().ContainSingle(error => error.Contains("contentHandle") && error.Contains("never a path"));

        var content = parameters with { Inputs = new[] { SourceAssetInput("content://a1b2", "audio/webm") } };
        WorkerProtocolValidator.ValidateRunParams(content).Should().BeEmpty();
    }

    [Fact]
    public void ValidateRunParams_ShouldRejectMissingInputsAndDuplicateIds()
    {
        WorkerProtocolValidator.ValidateRunParams(
                new ProcessorRunParams(WorkerProtocol.Version, ProcessingCapability.TextNormalize, null, null, null))
            .Should().ContainSingle(error => error == "params.inputs: at least one input is required");

        var shared = SourceAssetInput();
        var duplicated = WorkerProtocolValidator.ValidateRunParams(
            new ProcessorRunParams(WorkerProtocol.Version, ProcessingCapability.TextNormalize, new[] { shared, shared, null! }, null, null));
        duplicated.Should().Contain(error => error.Contains("inputs[1].id") && error.Contains("referenced twice"));
        duplicated.Should().Contain("params.inputs[2]: must not be null");
    }

    [Fact]
    public void ValidateResponseEnvelope_ShouldEnforceJsonRpcRules()
    {
        var ok = WorkerProtocol.Deserialize<JsonRpcResponse<ProcessorRunResult>>(PocResult);
        WorkerProtocolValidator.ValidateResponseEnvelope(ok, expectedId: "job-7f41").Should().BeEmpty();

        var wrongId = WorkerProtocolValidator.ValidateResponseEnvelope(ok, expectedId: "job-other");
        wrongId.Should().ContainSingle(error => error.Contains("expected 'job-other'"));

        var both = new JsonRpcResponse<ProcessorRunResult>(
            "1.0",
            "",
            ok!.Result,
            new JsonRpcError(WorkerProtocolErrorCodes.ProcessorFailure, "", null));
        var errors = WorkerProtocolValidator.ValidateResponseEnvelope(both);

        errors.Should().Contain(error => error.Contains("jsonrpc"));
        errors.Should().Contain("response.id: required");
        errors.Should().Contain("response: exactly one of result or error must be present");
        errors.Should().Contain("response.error.message: required");

        var neither = new JsonRpcResponse<ProcessorRunResult>(WorkerProtocol.JsonRpcVersion, "job-1", null, null);
        WorkerProtocolValidator.ValidateResponseEnvelope(neither)
            .Should().ContainSingle(error => error.Contains("exactly one of result or error"));
    }

    [Fact]
    public void ProtocolMessages_ShouldTolerateUnknownMembersUnlikeManifests()
    {
        var json = PocProgress.Replace("\"messageCode\": \"audio.transcribing\"", "\"messageCode\": \"audio.transcribing\", \"futureField\": 1");

        var notification = WorkerProtocol.Deserialize<JsonRpcNotification<ProcessorProgressParams>>(json);

        notification!.Params.MessageCode.Should().Be("audio.transcribing");
    }

    [Fact]
    public void ValidateResult_ShouldRejectMalformedSegmentsAndStatuses()
    {
        var result = new ProcessorRunResult(
            "done",
            new ProcessorIdentity("", "", null, null),
            new ProcessorOutput[]
            {
                new ProcessorRepresentationOutput(
                    "Transcript",
                    0,
                    "en",
                    "short text",
                    new[]
                    {
                        new ProcessorSegmentOutput(0, 6, 100, 50, null, 1.5),
                        new ProcessorSegmentOutput(4, 40, -1, null, null, null)
                    },
                    null,
                    null)
            },
            null,
            null);

        var errors = WorkerProtocolValidator.ValidateResult(result);

        errors.Should().Contain(error => error.StartsWith("result.status"));
        errors.Should().Contain("result.processor.id: required");
        errors.Should().Contain("result.processor.version: required");
        errors.Should().Contain(error => error.Contains("schemaVersion"));
        errors.Should().Contain(error => error.Contains("segments[0]") && error.Contains("endMs cannot precede startMs"));
        errors.Should().Contain(error => error.Contains("segments[0].confidence"));
        errors.Should().Contain(error => error.Contains("segments[1]") && error.Contains("must not overlap"));
        errors.Should().Contain(error => error.Contains("segments[1]") && error.Contains("charEnd <= text.length"));
        errors.Should().Contain(error => error.Contains("segments[1]") && error.Contains("timestamps cannot be negative"));
    }

    [Fact]
    public void ValidateResult_ShouldRequireAUsableOutputAndAConfigurationHashOnCompletion()
    {
        var result = new ProcessorRunResult(
            WorkerProtocol.StatusCompleted,
            CompletedIdentity(),
            Array.Empty<ProcessorOutput>(),
            null,
            null);

        WorkerProtocolValidator.ValidateResult(result)
            .Should().ContainSingle(error => error.Contains("at least one representation or candidate batch"));

        var diagnosticsOnly = result with { Outputs = new ProcessorOutput[] { new ProcessorDiagnosticOutput("X", WorkerProtocol.SeverityInfo, null) } };
        WorkerProtocolValidator.ValidateResult(diagnosticsOnly)
            .Should().ContainSingle(error => error.Contains("at least one representation or candidate batch"));

        var noHash = result with { Processor = new ProcessorIdentity("taskdeck.mock", "1.0.0", null, null) };
        WorkerProtocolValidator.ValidateResult(noHash)
            .Should().Contain(error => error.Contains("configurationHash: required on a completed run"));

        var cancelled = result with { Status = WorkerProtocol.StatusCancelled, Processor = noHash.Processor };
        WorkerProtocolValidator.ValidateResult(cancelled).Should().BeEmpty("a cancelled run carries no output to identify");
    }

    [Fact]
    public void ValidateResult_ShouldRejectNullEntriesNumericKindsNegativeUsageAndNullWarningsWithoutThrowing()
    {
        var json = PocResult
            .Replace("\"kind\": \"Transcript\"", "\"kind\": \"1\"")
            .Replace("\"peakVramMb\": 5740", "\"peakVramMb\": -1")
            .Replace("\"outputs\": [", "\"outputs\": [ null,")
            .Replace("\"segments\": [", "\"segments\": [ null,")
            .Replace("\"warnings\": []", "\"warnings\": [ null ]");
        var response = WorkerProtocol.Deserialize<JsonRpcResponse<ProcessorRunResult>>(json);

        var act = () => WorkerProtocolValidator.ValidateResult(response!.Result);

        var errors = act.Should().NotThrow("untrusted sidecar output must never abort the host").Subject;
        errors.Should().Contain("result.outputs[0]: must not be null");
        errors.Should().Contain(error => error.Contains("outputs[1].kind") && error.Contains("'1' is not a RepresentationKind"));
        errors.Should().Contain("result.outputs[1].segments[0]: must not be null");
        errors.Should().Contain("result.usage.peakVramMb: cannot be negative");
        errors.Should().Contain("result.warnings[0]: must not be null");
    }

    [Fact]
    public void ValidateResult_ShouldTypeThePayloadByKind()
    {
        var structuredKindWithText = TextOutput(nameof(RepresentationKind.DocumentStructure));
        var textKindWithoutText = TextOutput(nameof(RepresentationKind.OcrText), text: null);
        var structured = new ProcessorRepresentationOutput(
            nameof(RepresentationKind.StructuredEvent), 1, null, null, null, null,
            JsonDocument.Parse("{\"event\":\"meeting.created\"}").RootElement);

        var errors = WorkerProtocolValidator.ValidateResult(new ProcessorRunResult(
            WorkerProtocol.StatusCompleted, CompletedIdentity(),
            new ProcessorOutput[] { structuredKindWithText, textKindWithoutText, structured }, null, null));

        errors.Should().Contain(error => error.Contains("outputs[0].structured") && error.Contains("DocumentStructure"));
        errors.Should().Contain(error => error.Contains("outputs[1].text") && error.Contains("OcrText"));
        errors.Should().NotContain(error => error.Contains("outputs[2]"));
    }

    [Fact]
    public void ValidateResult_ShouldCheckRegionGeometry()
    {
        var output = new ProcessorRepresentationOutput(
            nameof(RepresentationKind.OcrText), 1, "en", "hello world", null,
            new[]
            {
                new ProcessorRegionOutput(1, 0.1, 0.1, 0.5, 0.2, 0, 5, 0.9),
                new ProcessorRegionOutput(0, 0.8, 0.0, 0.5, 0.2, null, null, 1.2),
                new ProcessorRegionOutput(null, 0.0, 0.0, 0.1, 0.1, 6, 40, null)
            },
            null);

        var errors = WorkerProtocolValidator.ValidateResult(new ProcessorRunResult(
            WorkerProtocol.StatusCompleted, CompletedIdentity(), new ProcessorOutput[] { output }, null, null));

        errors.Should().NotContain(error => error.Contains("regions[0]"));
        errors.Should().Contain(error => error.Contains("regions[1].pageNumber"));
        errors.Should().Contain(error => error.Contains("regions[1]") && error.Contains("normalised"));
        errors.Should().Contain(error => error.Contains("regions[1].confidence"));
        errors.Should().Contain(error => error.Contains("regions[2]") && error.Contains("charEnd <= text.length"));
    }

    [Fact]
    public void ValidateResult_ShouldCheckCandidateBatches()
    {
        var batch = new ProcessorCandidateBatchOutput(1, new[]
        {
            new ProcessorCandidateOutput(
                "Todo", " ", JsonDocument.Parse("[]").RootElement,
                new[]
                {
                    new ProcessorEvidenceReference(Guid.NewGuid(), 0, nameof(EvidenceAnchorKind.TextSpan), null, 0, 3, null, null, null, null, null),
                    new ProcessorEvidenceReference(null, 0, nameof(EvidenceAnchorKind.TextSpan), null, 0, 999, null, null, null, null, null),
                    new ProcessorEvidenceReference(null, 1, nameof(EvidenceAnchorKind.TimeRange), null, null, null, 5, 2, null, null, null),
                    new ProcessorEvidenceReference(null, 0, "Span", null, 0, 1, null, null, null, null, null),
                    new ProcessorEvidenceReference(null, 0, nameof(EvidenceAnchorKind.PageRegion), null, null, null, null, null, null, null, null),
                    new ProcessorEvidenceReference(null, 0, nameof(EvidenceAnchorKind.JsonPointer), null, null, null, null, null, null, null, "no-slash")
                },
                "guessed", 1.5),
            new ProcessorCandidateOutput(nameof(SemanticCandidateKind.Decision), "Ship on Friday", null, null, WorkerProtocol.DerivationExtractive, null),
            null!
        });

        var errors = WorkerProtocolValidator.ValidateResult(new ProcessorRunResult(
            WorkerProtocol.StatusCompleted, CompletedIdentity(), new ProcessorOutput[] { TextOutput(), batch }, null, null));

        errors.Should().Contain(error => error.Contains("candidates[0].kind") && error.Contains("'Todo'"));
        errors.Should().Contain(error => error.Contains("candidates[0].statement"));
        errors.Should().Contain(error => error.Contains("candidates[0].fields: must be an object"));
        errors.Should().Contain(error => error.Contains("candidates[0].derivation"));
        errors.Should().Contain(error => error.Contains("candidates[0].confidence"));
        errors.Should().Contain(error => error.Contains("evidence[0]") && error.Contains("exactly one of representationId or outputIndex"));
        errors.Should().Contain(error => error.Contains("evidence[1]") && error.Contains("past the referenced text"));
        errors.Should().Contain(error => error.Contains("evidence[2].outputIndex") && error.Contains("representation output"));
        errors.Should().Contain(error => error.Contains("evidence[2]") && error.Contains("TimeRange"));
        errors.Should().Contain(error => error.Contains("evidence[3].anchorKind") && error.Contains("'Span'"));
        errors.Should().Contain(error => error.Contains("evidence[4]") && error.Contains("PageRegion"));
        errors.Should().Contain(error => error.Contains("evidence[5]") && error.Contains("JsonPointer"));
        errors.Should().Contain(error => error.Contains("candidates[1].evidence") && error.Contains("extractive candidate must cite"));
        errors.Should().Contain("result.outputs[1].candidates[2]: must not be null");
    }

    [Fact]
    public void ValidateResult_ShouldCheckBillingUsageFields()
    {
        var usage = new ProcessorUsage(10, null, -1, null, null, null, 2.5m, null, 0.01m, "usd", null, null);

        var errors = WorkerProtocolValidator.ValidateResult(new ProcessorRunResult(
            WorkerProtocol.StatusCompleted, CompletedIdentity(), new ProcessorOutput[] { TextOutput() }, null, usage));

        errors.Should().Contain("result.usage.inputTokens: cannot be negative");
        errors.Should().Contain("result.usage.billableUnitKind: required when billableUnits is reported");
        errors.Should().Contain(error => error.Contains("result.usage.currency"));
    }

    [Fact]
    public void ValidateProgressAndCancel_ShouldRequireCorrelation()
    {
        WorkerProtocolValidator.ValidateProgress(new ProcessorProgressParams("job-1", "transcribing", 0.5, null)).Should().BeEmpty();
        WorkerProtocolValidator.ValidateProgress(new ProcessorProgressParams("job-1", "transcribing", null, null)).Should().BeEmpty();

        var errors = WorkerProtocolValidator.ValidateProgress(new ProcessorProgressParams("", " ", 1.5, null));

        errors.Should().Contain("progress.jobId: required");
        errors.Should().Contain("progress.phase: required");
        errors.Should().Contain("progress.fraction: must be within [0, 1]");
        WorkerProtocolValidator.ValidateProgress(null).Should().ContainSingle(error => error == "progress: missing");

        WorkerProtocolValidator.ValidateCancel(new ProcessorCancelParams("job-1")).Should().BeEmpty();
        WorkerProtocolValidator.ValidateCancel(new ProcessorCancelParams(" ")).Should().ContainSingle(error => error == "cancel.jobId: required");
        WorkerProtocolValidator.ValidateCancel(null).Should().ContainSingle(error => error == "cancel: missing");
    }

    [Fact]
    public void JsonRpcResponse_IsSuccess_ShouldRequireAResultAndNoError()
    {
        var ok = WorkerProtocol.Deserialize<JsonRpcResponse<ProcessorRunResult>>(PocResult)!;
        var failure = WorkerProtocol.Deserialize<JsonRpcResponse<ProcessorRunResult>>(PocFailure)!;
        var empty = new JsonRpcResponse<ProcessorRunResult>(WorkerProtocol.JsonRpcVersion, "job-1", null, null);

        ok.IsSuccess.Should().BeTrue();
        failure.IsSuccess.Should().BeFalse();
        empty.IsSuccess.Should().BeFalse();
        empty.IsError.Should().BeFalse("neither member present is malformed, not an error result");
    }

    [Fact]
    public void ErrorCodes_ShouldStayInTheServerDefinedRange()
    {
        var codes = new[]
        {
            WorkerProtocolErrorCodes.ProcessorFailure,
            WorkerProtocolErrorCodes.UnsupportedCapability,
            WorkerProtocolErrorCodes.UnsupportedMediaType,
            WorkerProtocolErrorCodes.UnsupportedInput,
            WorkerProtocolErrorCodes.ResourceExhausted,
            WorkerProtocolErrorCodes.DeadlineExceeded,
            WorkerProtocolErrorCodes.OutputTooLarge,
            WorkerProtocolErrorCodes.Cancelled,
            WorkerProtocolErrorCodes.ProtocolVersionMismatch
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(code => code <= -32000 && code >= -32099);
    }

    [Fact]
    public void Stability_ShouldBeMarkedAlphaUntilConformance()
    {
        WorkerProtocol.Stability.Should().Be("v1-alpha");
        WorkerProtocol.Version.Should().Be(1);
    }
}
