using FluentAssertions;
using Taskdeck.Application.Processing.Protocol;
using Taskdeck.Domain.Processing;
using Xunit;

namespace Taskdeck.Application.Tests.Processing;

public sealed class WorkerProtocolSerializationTests
{
    private const string ValidSha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    /// <summary>The proof-of-concept job request from the 2026-08-30 pack, with a real digest.</summary>
    private const string PocRequest = """
        {
          "jsonrpc": "2.0",
          "id": "job-7f41",
          "method": "processor.run",
          "params": {
            "protocolVersion": 1,
            "capability": "audio.transcribe",
            "input": {
              "assetId": "7ec00b5e-7b9f-4ebd-8a31-f018340bb0aa",
              "mediaType": "audio/webm",
              "contentHandle": "spool://2b9e",
              "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
              "byteSize": 2481000
            },
            "options": {
              "language": "auto",
              "qualityTier": "balanced",
              "wordTimestamps": false,
              "diarization": false,
              "maxSpeakers": null
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
            "representations": [
              {
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
              }
            ],
            "warnings": [],
            "usage": {
              "wallTimeMs": 18420,
              "audioDurationMs": 181000,
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
        request.Params.Input!.ContentHandle.Should().Be("spool://2b9e");
        request.Params.Limits!.DeadlineUtc.Should().Be(new DateTimeOffset(2026, 8, 30, 3, 0, 0, TimeSpan.Zero));
        request.Params.Options!.MaxSpeakers.Should().BeNull();

        WorkerProtocolValidator.ValidateRunParams(request.Params).Should().BeEmpty();
    }

    [Fact]
    public void Request_ShouldRoundTripWithTheJsonRpcPropertyName()
    {
        var parameters = new ProcessorRunParams(
            WorkerProtocol.Version,
            ProcessingCapability.ImageOcr,
            new ProcessorRunInput(Guid.NewGuid(), "image/png", "spool://img", ValidSha, 1234),
            null,
            new ProcessorRunLimits(null, 30_000, null));
        var request = JsonRpcRequest<ProcessorRunParams>.Create("job-1", WorkerProtocol.RunMethod, parameters);

        var json = WorkerProtocol.Serialize(request);
        var back = WorkerProtocol.Deserialize<JsonRpcRequest<ProcessorRunParams>>(json);

        json.Should().Contain("\"jsonrpc\":\"2.0\"");
        json.Should().NotContain("\"options\"", "null members are omitted on the wire");
        back.Should().Be(request);
    }

    [Fact]
    public void PocProgress_ShouldDeserialize()
    {
        var notification = WorkerProtocol.Deserialize<JsonRpcNotification<ProcessorProgressParams>>(PocProgress);

        notification.Should().NotBeNull();
        notification!.Method.Should().Be(WorkerProtocol.ProgressMethod);
        notification.Params.Fraction.Should().BeApproximately(0.54, 0.0001);
        notification.Params.MessageCode.Should().Be("audio.transcribing");
    }

    [Fact]
    public void PocResult_ShouldDeserializeAndValidate()
    {
        var response = WorkerProtocol.Deserialize<JsonRpcResponse<ProcessorRunResult>>(PocResult);

        response.Should().NotBeNull();
        response!.IsError.Should().BeFalse();
        response.Result!.Status.Should().Be(WorkerProtocol.StatusCompleted);
        response.Result.Processor!.Model.Should().Be("large-v3-turbo");
        response.Result.Representations.Should().ContainSingle();
        response.Result.Representations![0].Segments.Should().HaveCount(2);
        response.Result.Usage!.PeakVramMb.Should().Be(5740);

        WorkerProtocolValidator.ValidateResult(response.Result).Should().BeEmpty();
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
            new ProcessorRunInput(Guid.Empty, "", "", "nope", 0),
            new ProcessorRunOptions(null, null, null, null, 0),
            new ProcessorRunLimits(null, 0, 0));

        var errors = WorkerProtocolValidator.ValidateRunParams(parameters);

        errors.Should().Contain(error => error.Contains("protocolVersion"));
        errors.Should().Contain(error => error.Contains("not a known capability"));
        errors.Should().Contain(error => error.Contains("assetId"));
        errors.Should().Contain(error => error.Contains("mediaType"));
        errors.Should().Contain(error => error.Contains("contentHandle"));
        errors.Should().Contain(error => error.Contains("sha256"));
        errors.Should().Contain(error => error.Contains("byteSize"));
        errors.Should().Contain(error => error.Contains("maxWallTimeMs"));
        errors.Should().Contain(error => error.Contains("maxOutputBytes"));
        errors.Should().Contain(error => error.Contains("maxSpeakers"));
    }

    [Fact]
    public void ValidateRunParams_ShouldRejectMissingInput()
    {
        var errors = WorkerProtocolValidator.ValidateRunParams(
            new ProcessorRunParams(WorkerProtocol.Version, ProcessingCapability.TextNormalize, null, null, null));

        errors.Should().ContainSingle(error => error == "params.input: required");
    }

    [Fact]
    public void ValidateResult_ShouldRejectMalformedSegmentsAndStatuses()
    {
        var result = new ProcessorRunResult(
            "done",
            new ProcessorIdentity("", "", null, null),
            new[]
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
                    })
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
    public void ValidateResult_ShouldRequireARepresentationOnCompletion()
    {
        var result = new ProcessorRunResult(
            WorkerProtocol.StatusCompleted,
            new ProcessorIdentity("taskdeck.mock", "1.0.0", null, null),
            Array.Empty<ProcessorRepresentationOutput>(),
            null,
            null);

        WorkerProtocolValidator.ValidateResult(result)
            .Should().ContainSingle(error => error.Contains("must emit at least one representation"));

        var cancelled = result with { Status = WorkerProtocol.StatusCancelled };
        WorkerProtocolValidator.ValidateResult(cancelled).Should().BeEmpty();
    }

    [Fact]
    public void ErrorCodes_ShouldStayInTheServerDefinedRange()
    {
        var codes = new[]
        {
            WorkerProtocolErrorCodes.ProcessorFailure,
            WorkerProtocolErrorCodes.UnsupportedCapability,
            WorkerProtocolErrorCodes.UnsupportedMediaType,
            WorkerProtocolErrorCodes.ResourceExhausted,
            WorkerProtocolErrorCodes.DeadlineExceeded,
            WorkerProtocolErrorCodes.OutputTooLarge,
            WorkerProtocolErrorCodes.Cancelled,
            WorkerProtocolErrorCodes.ProtocolVersionMismatch
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(code => code <= -32000 && code >= -32099);
    }
}
