using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CaptureRequestContractTests
{
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

        var result = CaptureRequestContract.ParsePayload(payload);

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

        var result = CaptureRequestContract.ParsePayload(payload);

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
}
