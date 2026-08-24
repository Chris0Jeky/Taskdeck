using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CaptureRequestContractTests
{
    public static IEnumerable<object[]> InvalidCaptureLabels()
    {
        yield return [new[] { "" }, "empty values"];
        yield return
        [
            new[] { new string('l', CaptureRequestContract.MaxLabelNameLength + 1) },
            $"cannot exceed {CaptureRequestContract.MaxLabelNameLength}"
        ];
        yield return
        [
            Enumerable.Range(0, CaptureRequestContract.MaxLabelCount + 1)
                .Select(index => $"label-{index}")
                .ToArray(),
            $"more than {CaptureRequestContract.MaxLabelCount}"
        ];
    }

    [Fact]
    public void ParsePayload_ShouldTreatPlainTextAsCapturePayload()
    {
        var result = CaptureRequestContract.ParsePayload("quick note for later");

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(CaptureRequestContract.CurrentSchemaVersion);
        result.Value.Source.Should().Be(CaptureSource.Typed);
        result.Value.Text.Should().Be("quick note for later");
    }

    [Fact]
    public void ParsePayload_ShouldTreatMalformedJsonPrefixAsPlainTextPayload()
    {
        var payload = "{not-json payload";

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be(CaptureSource.Typed);
        result.Value.Text.Should().Be(payload);
    }

    [Fact]
    public void ParsePayload_ShouldParseJsonPayloadCaseInsensitive()
    {
        var payload = """
                      {
                        "Version": 1,
                        "Source": "paste",
                        "Text": "capture text",
                        "TitleHint": "Inbox",
                        "ExternalRef": "https://example.com/ref"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be(CaptureSource.Paste);
        result.Value.Text.Should().Be("capture text");
        result.Value.TitleHint.Should().Be("Inbox");
    }

    [Fact]
    public void ParsePayload_ShouldKeepNewMetadataOptionalForOlderPayloads()
    {
        const string olderPayload = """
                                    {
                                      "version": 1,
                                      "source": "typed",
                                      "text": "legacy capture"
                                    }
                                    """;

        var parsedOlderPayload = CaptureRequestContract.ParsePayload(olderPayload);

        parsedOlderPayload.IsSuccess.Should().BeTrue();
        parsedOlderPayload.Value.DueDate.Should().BeNull();
        parsedOlderPayload.Value.Labels.Should().BeNull();

        var serialized = CaptureRequestContract.SerializePayload(new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "new capture",
            DueDate: new DateOnly(2026, 8, 23),
            Labels: ["shopping"]));
        var roundTripped = CaptureRequestContract.ParseStoredPayload(serialized);

        roundTripped.DueDate.Should().Be(new DateOnly(2026, 8, 23));
        roundTripped.Labels.Should().Equal("shopping");
    }

    [Theory]
    [MemberData(nameof(InvalidCaptureLabels))]
    public void ValidatePayload_ShouldRejectInvalidLabels(string[] labels, string expectedMessage)
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "bounded capture",
            Labels: labels);

        var result = CaptureRequestContract.ValidatePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain(expectedMessage);
    }

    [Fact]
    public void ValidatePayload_ShouldAcceptLabelsAtAggregateBoundary()
    {
        var labels = Enumerable.Range(0, CaptureRequestContract.MaxLabelCount)
            .Select(index => index.ToString().PadLeft(CaptureRequestContract.MaxLabelNameLength, 'l'))
            .ToArray();
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "bounded capture",
            Labels: labels);

        var result = CaptureRequestContract.ValidatePayload(payload);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Labels.Should().HaveCount(CaptureRequestContract.MaxLabelCount);
        result.Value.Labels.Should().OnlyContain(label => label.Length == CaptureRequestContract.MaxLabelNameLength);
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenTextExceedsMaxLength()
    {
        var longText = new string('a', CaptureRequestContract.MaxRawTextLength + 1);

        var result = CaptureRequestContract.ParsePayload(longText);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("cannot exceed");
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenSchemaVersionIsUnsupported()
    {
        var payload = """
                      {
                        "version": 2,
                        "text": "capture text"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported capture payload schema version");
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenActorIdentityFieldIsSupplied()
    {
        var payload = """
                      {
                        "version": 1,
                        "source": "typed",
                        "text": "capture text",
                        "ownerUserId": "3d15cc55-eb69-4974-ad8a-298ef43f9b55"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("must not include actor identity field");
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenProvenanceAttributionFieldIsSuppliedInUntrustedPayload()
    {
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "requestedByUserId": "{{Guid.NewGuid()}}"
                          }
                        }
                        """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("must not include server attribution field");
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenDuplicateProvenanceContainsForbiddenAttributionField()
    {
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": null,
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "requestedByUserId": "{{Guid.NewGuid()}}"
                          }
                        }
                        """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("must not include server attribution field");
    }

    [Theory]
    [InlineData("proposalId")]
    [InlineData("triageRunId")]
    public void ParsePayload_ShouldFail_WhenTriageLinkageFieldIsSuppliedInUntrustedPayload(string field)
    {
        // A client-supplied provenance.proposalId would make the workers short-circuit and mark
        // the capture Completed WITHOUT ever triaging it; triageRunId is equally server-authored.
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "{{field}}": "{{Guid.NewGuid()}}"
                          }
                        }
                        """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("must not include server attribution field");
    }

    [Theory]
    [InlineData("provider", "OpenAI")]
    [InlineData("model", "gpt-4o-mini")]
    [InlineData("promptVersion", "llm-triage.v1")]
    public void ParsePayload_ShouldFail_WhenTriageEngineProvenanceIsSuppliedInUntrustedPayload(string field, string value)
    {
        // provider/model/promptVersion are stamped by the triage pipeline after it actually ran;
        // accepting them from a client would persist fabricated provenance (#1273).
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "{{field}}": "{{value}}"
                          }
                        }
                        """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("must not include server attribution field");
    }

    [Fact]
    public void ParsePayload_ShouldAllowTriageLinkageFields_WhenServerAttributionIsAllowed()
    {
        // The workers re-parse STAMPED payloads with allowServerAttributionFields: true; the
        // client-path hardening must not break server-side round-trips of triaged captures.
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "proposalId": "{{Guid.NewGuid()}}",
                            "triageRunId": "{{Guid.NewGuid()}}",
                            "provider": "OpenAI",
                            "model": "gpt-4o-mini",
                            "promptVersion": "llm-triage.v1"
                          }
                        }
                        """;

        var result = CaptureRequestContract.ParsePayload(payload, allowServerAttributionFields: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provenance!.Provider.Should().Be("OpenAI");
        result.Value.Provenance.PromptVersion.Should().Be("llm-triage.v1");
    }

    [Fact]
    public void ParsePayload_ShouldAllowProvenanceAttributionField_WhenServerAttributionIsAllowed()
    {
        var requestedByUserId = Guid.NewGuid();
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "requestedByUserId": "{{requestedByUserId}}",
                            "sourceSurface": "capture"
                          }
                        }
                        """;

        var result = CaptureRequestContract.ParsePayload(payload, allowServerAttributionFields: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provenance.Should().NotBeNull();
        result.Value.Provenance!.RequestedByUserId.Should().Be(requestedByUserId);
        result.Value.Provenance.SourceSurface.Should().Be("capture");
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenSourceIsInvalidString()
    {
        const string sensitiveSource = "Authorization: Bearer secret-token";
        var payload = """
                      {
                        "version": 1,
                        "source": "Authorization: Bearer secret-token",
                        "text": "capture text"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Invalid capture source value");
        result.ErrorMessage.Should().NotContain(sensitiveSource);
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenSourceIsOutOfRangeNumericString()
    {
        var payload = """
                      {
                        "version": 1,
                        "source": "9999",
                        "text": "capture text"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenSourceIsOutOfRangeNumericValue()
    {
        var payload = """
                      {
                        "version": 1,
                        "source": 9999,
                        "text": "capture text"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void WithProvenance_ShouldLinkCaptureToTriageAndProposal()
    {
        var captureId = Guid.NewGuid();
        var triageRunId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "capture text");

        var linked = CaptureRequestContract.WithProvenance(
            payload,
            captureId,
            triageRunId,
            proposalId,
            "triage.v1",
            "OpenAI",
            "gpt-4o-mini",
            requestedByUserId,
            "req-correlation",
            "capture");

        linked.Provenance.Should().NotBeNull();
        linked.Provenance!.CaptureItemId.Should().Be(captureId);
        linked.Provenance.TriageRunId.Should().Be(triageRunId);
        linked.Provenance.ProposalId.Should().Be(proposalId);
        linked.Provenance.PromptVersion.Should().Be("triage.v1");
        linked.Provenance.Provider.Should().Be("OpenAI");
        linked.Provenance.Model.Should().Be("gpt-4o-mini");
        linked.Provenance.RequestedByUserId.Should().Be(requestedByUserId);
        linked.Provenance.CorrelationId.Should().Be("req-correlation");
        linked.Provenance.SourceSurface.Should().Be("capture");
    }

    [Fact]
    public void WithProvenance_ShouldPreserveExistingAttribution_WhenOnlyTriageMetadataIsUpdated()
    {
        var captureId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "capture text",
            Provenance: new CaptureProvenanceV1(
                captureId,
                RequestedByUserId: requestedByUserId,
                CorrelationId: "req-preserve",
                SourceSurface: "capture"));

        var linked = CaptureRequestContract.WithProvenance(
            payload,
            captureId,
            triageRunId: Guid.NewGuid(),
            proposalId: Guid.NewGuid(),
            promptVersion: "triage.v1",
            provider: "Mock",
            model: "mock-default");

        linked.Provenance.Should().NotBeNull();
        linked.Provenance!.RequestedByUserId.Should().Be(requestedByUserId);
        linked.Provenance.CorrelationId.Should().Be("req-preserve");
        linked.Provenance.SourceSurface.Should().Be("capture");
    }

    [Fact]
    public void WithProvenance_ShouldPersistConvertedAt_WhenCaptureIsApplied()
    {
        var captureId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var convertedAt = DateTimeOffset.UtcNow;
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "capture text");

        var converted = CaptureRequestContract.WithProvenance(
            payload,
            captureId,
            proposalId: proposalId,
            convertedAt: convertedAt);

        converted.Provenance.Should().NotBeNull();
        converted.Provenance!.ProposalId.Should().Be(proposalId);
        converted.Provenance.ConvertedAt.Should().Be(convertedAt);
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenProvenanceProviderExceedsMaxLength()
    {
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "provider": "{{new string('p', CaptureRequestContract.MaxProviderLength + 1)}}"
                          }
                        }
                        """;

        // Server path: the client path now rejects the field outright as server-authored
        // provenance, so the length contract is exercised where stamped payloads round-trip.
        var result = CaptureRequestContract.ParsePayload(payload, allowServerAttributionFields: true);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Capture provider cannot exceed");
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenProvenanceModelExceedsMaxLength()
    {
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "model": "{{new string('m', CaptureRequestContract.MaxModelLength + 1)}}"
                          }
                        }
                        """;

        // Server path: the client path now rejects the field outright as server-authored
        // provenance, so the length contract is exercised where stamped payloads round-trip.
        var result = CaptureRequestContract.ParsePayload(payload, allowServerAttributionFields: true);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Capture model cannot exceed");
    }

    [Fact]
    public void ParsePayload_ShouldFail_WhenAttributionSourceSurfaceIsUnsupported()
    {
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "capture text",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "sourceSurface": "desktop-widget"
                          }
                        }
                        """;

        var result = CaptureRequestContract.ParsePayload(payload, allowServerAttributionFields: true);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported capture attribution source surface");
    }

    [Theory]
    [InlineData("vscode")]
    [InlineData("VsCode")]
    [InlineData("VSCODE")]
    public void ParsePayload_ShouldSucceed_WhenAttributionSourceSurfaceIsVsCode(string surface)
    {
        var payload = $$"""
                        {
                          "version": 1,
                          "text": "selected code from IDE",
                          "source": "VsCodeExtension",
                          "provenance": {
                            "captureItemId": "{{Guid.NewGuid()}}",
                            "sourceSurface": "{{surface}}"
                          }
                        }
                        """;

        var result = CaptureRequestContract.ParsePayload(payload, allowServerAttributionFields: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provenance.Should().NotBeNull();
        result.Value.Provenance!.SourceSurface.Should().Be(surface);
        result.Value.Source.Should().Be(CaptureSource.VsCodeExtension);
    }

    [Fact]
    public void WithProvenance_ShouldThrow_WhenCaptureItemIdIsEmpty()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "capture text");

        var act = () => CaptureRequestContract.WithProvenance(payload, Guid.Empty);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void SerializePayload_ShouldThrow_WhenPayloadIsInvalid()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            new string('x', CaptureRequestContract.MaxRawTextLength + 5));

        var act = () => CaptureRequestContract.SerializePayload(payload);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void SanitizeProvenanceMetadata_ShouldTrimAndClampValues()
    {
        var raw = $"  {new string('x', CaptureRequestContract.MaxProviderLength + 5)}  ";

        var sanitized = CaptureRequestContract.SanitizeProvenanceMetadata(
            raw,
            CaptureRequestContract.MaxProviderLength);

        sanitized.Should().HaveLength(CaptureRequestContract.MaxProviderLength);
        sanitized.Should().Be(new string('x', CaptureRequestContract.MaxProviderLength));
    }

    [Fact]
    public void SanitizeProvenanceMetadata_ShouldReturnFallback_WhenValueIsMissing()
    {
        var sanitized = CaptureRequestContract.SanitizeProvenanceMetadata(
            " ",
            CaptureRequestContract.MaxProviderLength);

        sanitized.Should().Be("unknown");
    }

    [Fact]
    public void IsTranscriptSource_ShouldReturnTrue_ForTranscriptPaste()
    {
        CaptureRequestContract.IsTranscriptSource(CaptureSource.TranscriptPaste).Should().BeTrue();
    }

    [Fact]
    public void IsTranscriptSource_ShouldReturnTrue_ForTranscriptFile()
    {
        CaptureRequestContract.IsTranscriptSource(CaptureSource.TranscriptFile).Should().BeTrue();
    }

    [Theory]
    [InlineData(CaptureSource.Typed)]
    [InlineData(CaptureSource.Paste)]
    [InlineData(CaptureSource.Import)]
    [InlineData(CaptureSource.Voice)]
    [InlineData(CaptureSource.MeetingIntegration)]
    [InlineData(CaptureSource.VsCodeExtension)]
    public void IsTranscriptSource_ShouldReturnFalse_ForNonTranscriptSources(CaptureSource source)
    {
        CaptureRequestContract.IsTranscriptSource(source).Should().BeFalse();
    }

    [Fact]
    public void ValidatePayload_ShouldAllowLargerText_ForTranscriptPasteSource()
    {
        var textLength = CaptureRequestContract.MaxRawTextLength + 1;
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.TranscriptPaste,
            new string('t', textLength));

        var result = CaptureRequestContract.ValidatePayload(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().HaveLength(textLength);
    }

    [Fact]
    public void ValidatePayload_ShouldAllowLargerText_ForTranscriptFileSource()
    {
        var textLength = CaptureRequestContract.MaxRawTextLength + 1;
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.TranscriptFile,
            new string('t', textLength));

        var result = CaptureRequestContract.ValidatePayload(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().HaveLength(textLength);
    }

    [Fact]
    public void ValidatePayload_ShouldFail_WhenTranscriptTextExceedsTranscriptMaxLength()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.TranscriptPaste,
            new string('t', CaptureRequestContract.MaxTranscriptTextLength + 1));

        var result = CaptureRequestContract.ValidatePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("cannot exceed");
        result.ErrorMessage.Should().Contain(CaptureRequestContract.MaxTranscriptTextLength.ToString());
    }

    [Fact]
    public void ValidatePayload_ShouldAcceptTranscriptTextAtExactMaxLength()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.TranscriptFile,
            new string('t', CaptureRequestContract.MaxTranscriptTextLength));

        var result = CaptureRequestContract.ValidatePayload(payload);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ParsePayload_ShouldParseTranscriptFileSource()
    {
        var payload = """
                      {
                        "version": 1,
                        "source": "transcriptFile",
                        "text": "meeting transcript content"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be(CaptureSource.TranscriptFile);
        result.Value.Text.Should().Be("meeting transcript content");
    }

    [Fact]
    public void ParsePayload_ShouldParseTranscriptFileSourceAsNumeric()
    {
        var payload = """
                      {
                        "version": 1,
                        "source": 6,
                        "text": "meeting transcript content"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be(CaptureSource.TranscriptFile);
    }

    [Fact]
    public void ValidatePayload_ShouldEnforceStandardLimit_ForNonTranscriptSources()
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            new string('x', CaptureRequestContract.MaxRawTextLength + 1));

        var result = CaptureRequestContract.ValidatePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain(CaptureRequestContract.MaxRawTextLength.ToString());
    }

    [Theory]
    [InlineData(CaptureRequestContract.RequestTypeV1)]
    [InlineData(CaptureRequestContract.RequestTypeTranscriptV1)]
    [InlineData("INBOX.CAPTURE.TRANSCRIPT.V1")]
    [InlineData("Inbox.Capture.Transcript.v1")]
    public void ValidateRequestType_ShouldAcceptSupportedCaptureRequestTypes(string requestType)
    {
        var result = CaptureRequestContract.ValidateRequestType(requestType);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("inbox.capture.v2")]
    [InlineData("inbox.capture.transcript.v2")]
    [InlineData("inbox.capture.unknown")]
    public void ValidateRequestType_ShouldFail_ForUnsupportedCapturePrefixedTypes(string requestType)
    {
        var result = CaptureRequestContract.ValidateRequestType(requestType);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported capture request type");
        result.ErrorMessage.Should().Contain(CaptureRequestContract.RequestTypeV1);
        result.ErrorMessage.Should().Contain(CaptureRequestContract.RequestTypeTranscriptV1);
    }

    [Theory]
    [InlineData(CaptureRequestContract.RequestTypeTranscriptV1)]
    [InlineData("INBOX.CAPTURE.TRANSCRIPT.V1")]
    [InlineData("Inbox.Capture.Transcript.V1")]
    public void IsTranscriptRequestType_ShouldReturnTrue_ForTranscriptRequestTypes(string requestType)
    {
        CaptureRequestContract.IsTranscriptRequestType(requestType).Should().BeTrue();
    }

    [Theory]
    [InlineData(CaptureRequestContract.RequestTypeV1)]
    [InlineData("automation.chat.v1")]
    [InlineData("transcript.v1")]
    public void IsTranscriptRequestType_ShouldReturnFalse_ForNonTranscriptRequestTypes(string requestType)
    {
        CaptureRequestContract.IsTranscriptRequestType(requestType).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTranscriptRequestType_ShouldReturnFalse_ForMissingRequestTypes(string? requestType)
    {
        CaptureRequestContract.IsTranscriptRequestType(requestType!).Should().BeFalse();
    }

    [Theory]
    [InlineData(CaptureSource.TranscriptPaste)]
    [InlineData(CaptureSource.TranscriptFile)]
    public void ResolveRequestTypeForSource_ShouldReturnTranscriptV1_ForTranscriptSources(CaptureSource source)
    {
        CaptureRequestContract.ResolveRequestTypeForSource(source)
            .Should().Be(CaptureRequestContract.RequestTypeTranscriptV1);
    }

    [Theory]
    [InlineData(CaptureSource.Typed)]
    [InlineData(CaptureSource.Paste)]
    [InlineData(CaptureSource.Import)]
    [InlineData(CaptureSource.Voice)]
    [InlineData(CaptureSource.MeetingIntegration)]
    [InlineData(CaptureSource.VsCodeExtension)]
    public void ResolveRequestTypeForSource_ShouldReturnCaptureV1_ForNonTranscriptSources(CaptureSource source)
    {
        CaptureRequestContract.ResolveRequestTypeForSource(source)
            .Should().Be(CaptureRequestContract.RequestTypeV1);
    }
}
