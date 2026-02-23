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
    public void ParsePayload_ShouldFail_WhenSourceIsInvalidString()
    {
        var payload = """
                      {
                        "version": 1,
                        "source": "invalid_source",
                        "text": "capture text"
                      }
                      """;

        var result = CaptureRequestContract.ParsePayload(payload);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Invalid capture source");
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
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            "capture text");

        var linked = CaptureRequestContract.WithProvenance(
            payload,
            captureId,
            triageRunId,
            proposalId,
            "triage.v1");

        linked.Provenance.Should().NotBeNull();
        linked.Provenance!.CaptureItemId.Should().Be(captureId);
        linked.Provenance.TriageRunId.Should().Be(triageRunId);
        linked.Provenance.ProposalId.Should().Be(proposalId);
        linked.Provenance.PromptVersion.Should().Be("triage.v1");
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
}
