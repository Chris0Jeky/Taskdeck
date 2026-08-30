using FluentAssertions;
using Taskdeck.Application.Processing;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Processing;

public sealed class ProcessorManifestValidatorTests
{
    /// <summary>The WhisperX example manifest shipped with the 2026-08-30 planning pack, verbatim.</summary>
    private const string WhisperXManifest = """
        {
          "id": "taskdeck.whisperx",
          "version": "1.0.0",
          "displayName": "WhisperX Local",
          "capabilities": ["audio.transcribe", "audio.align", "audio.diarize"],
          "execution": "sidecar",
          "locality": "local",
          "accepts": ["audio/*", "video/mp4", "video/webm"],
          "languages": ["*"],
          "features": ["vad", "segment-timestamps", "word-timestamps", "speaker-labels", "cpu-fallback"],
          "resources": {
            "cpu": true,
            "gpu": "optional",
            "minVramMb": 0,
            "estimatedRamMb": 4096
          },
          "privacy": {
            "networkRequired": false,
            "allowedHosts": [],
            "dataClasses": ["audio", "metadata"],
            "supportsRegionalRouting": false
          },
          "costModel": {
            "type": "compute-time",
            "currency": "USD",
            "unitPrice": 0
          },
          "outputSchemas": [
            "https://taskdeck.dev/schemas/transcript-representation.v1.json"
          ]
        }
        """;

    private static ProcessorManifest ParseExample()
    {
        ProcessorManifest.TryParse(WhisperXManifest, out var manifest, out var error).Should().BeTrue(error);
        return manifest!;
    }

    [Fact]
    public void ExampleManifest_ShouldParseWithTypedEnums()
    {
        var manifest = ParseExample();

        manifest.Id.Should().Be("taskdeck.whisperx");
        manifest.Execution.Should().Be(ProcessorExecutionMode.Sidecar);
        manifest.Locality.Should().Be(ProcessorLocality.Local);
        manifest.Resources!.Gpu.Should().Be(ProcessorGpuRequirement.Optional);
        manifest.CostModel!.Type.Should().Be(ProcessorCostModelType.ComputeTime);
        manifest.Capabilities.Should().Equal("audio.transcribe", "audio.align", "audio.diarize");
    }

    [Fact]
    public void ExampleManifest_ShouldBeValid()
    {
        var result = ProcessorManifestValidator.Validate(ParseExample());

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldRejectMissingManifest()
    {
        ProcessorManifestValidator.Validate(null).Errors.Should().ContainSingle(error => error.StartsWith("manifest"));
    }

    [Theory]
    [InlineData("WhisperX")]
    [InlineData("taskdeck..whisperx")]
    [InlineData("-taskdeck")]
    [InlineData("")]
    public void Validate_ShouldRejectMalformedIds(string id)
    {
        var manifest = ParseExample() with { Id = id };

        ProcessorManifestValidator.Validate(manifest).Errors.Should().Contain(error => error.StartsWith("id:"));
    }

    [Fact]
    public void Validate_ShouldRejectUnknownAndDuplicateCapabilities()
    {
        var manifest = ParseExample() with { Capabilities = new[] { "audio.transcribe", "board.mutate", "audio.transcribe" } };

        var errors = ProcessorManifestValidator.Validate(manifest).Errors;

        errors.Should().Contain(error => error.Contains("'board.mutate' is not a known capability"));
        errors.Should().Contain(error => error.Contains("'audio.transcribe' is declared twice"));
    }

    [Fact]
    public void Validate_ShouldRejectHostsOnALocalOnlyProcessor()
    {
        var manifest = ParseExample() with
        {
            Privacy = new ProcessorPrivacyDeclaration(false, new[] { "api.example.com" }, null, null)
        };

        ProcessorManifestValidator.Validate(manifest).Errors
            .Should().Contain(error => error.Contains("networkRequired=false cannot declare allowedHosts"));
    }

    [Fact]
    public void Validate_ShouldRequireNetworkAndHostsForARemoteProcessor()
    {
        var manifest = ParseExample() with
        {
            Execution = ProcessorExecutionMode.Remote,
            Locality = ProcessorLocality.Remote,
            Privacy = new ProcessorPrivacyDeclaration(false, null, null, null)
        };

        ProcessorManifestValidator.Validate(manifest).Errors
            .Should().Contain(error => error.Contains("remote processor must declare networkRequired=true"));
    }

    [Fact]
    public void Validate_ShouldRejectALocalProcessorThatNeedsTheNetwork()
    {
        var manifest = ParseExample() with
        {
            Privacy = new ProcessorPrivacyDeclaration(true, new[] { "api.example.com" }, null, null)
        };

        ProcessorManifestValidator.Validate(manifest).Errors
            .Should().Contain(error => error.Contains("local processor cannot declare networkRequired=true"));
    }

    [Fact]
    public void Validate_ShouldAcceptAWellFormedRemoteProcessor()
    {
        var manifest = ParseExample() with
        {
            Id = "taskdeck.cloud-stt",
            Execution = ProcessorExecutionMode.Remote,
            Locality = ProcessorLocality.Remote,
            Resources = new ProcessorResourceRequirements(false, ProcessorGpuRequirement.None, null, null),
            Privacy = new ProcessorPrivacyDeclaration(true, new[] { "api.deepgram.com" }, new[] { "audio" }, true),
            CostModel = new ProcessorCostModel(ProcessorCostModelType.PerMinute, "USD", 0.0043m)
        };

        ProcessorManifestValidator.Validate(manifest).Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldRequireResourcesAndPrivacy()
    {
        var manifest = ParseExample() with { Resources = null, Privacy = null };

        var errors = ProcessorManifestValidator.Validate(manifest).Errors;

        errors.Should().Contain("resources: required");
        errors.Should().Contain("privacy: required");
    }

    [Fact]
    public void Validate_ShouldRejectInconsistentCostAndResourceDeclarations()
    {
        var manifest = ParseExample() with
        {
            Resources = new ProcessorResourceRequirements(true, ProcessorGpuRequirement.None, 2048, null),
            CostModel = new ProcessorCostModel(ProcessorCostModelType.FreeLocal, "usd", 1.5m)
        };

        var errors = ProcessorManifestValidator.Validate(manifest).Errors;

        errors.Should().Contain(error => error.Contains("declares no GPU cannot require VRAM"));
        errors.Should().Contain(error => error.Contains("three-letter ISO code"));
        errors.Should().Contain(error => error.Contains("free-local processor cannot declare a unit price"));
    }

    [Fact]
    public void Validate_ShouldRejectUnknownDataClasses()
    {
        var manifest = ParseExample() with
        {
            Privacy = new ProcessorPrivacyDeclaration(false, null, new[] { "audio", "biometric" }, null)
        };

        ProcessorManifestValidator.Validate(manifest).Errors
            .Should().Contain(error => error.Contains("'biometric' is not one of"));
    }

    [Fact]
    public void TryParse_ShouldReportMalformedJsonWithoutThrowing()
    {
        ProcessorManifest.TryParse("{ not json", out var manifest, out var error).Should().BeFalse();
        manifest.Should().BeNull();
        error.Should().StartWith("Manifest JSON is malformed");

        ProcessorManifest.TryParse("   ", out _, out var emptyError).Should().BeFalse();
        emptyError.Should().Be("Manifest JSON is empty");
    }

    [Fact]
    public void TryParse_ShouldRejectUnknownMembersLikeTheSchemaDoes()
    {
        // additionalProperties: false in processor-manifest.v1.schema.json — a typo'd or unknown
        // field must not parse clean.
        var json = WhisperXManifest.Replace("\"displayName\": \"WhisperX Local\",", "\"displayName\": \"WhisperX Local\", \"capabilites\": [],");

        ProcessorManifest.TryParse(json, out _, out var error).Should().BeFalse();
        error.Should().StartWith("Manifest JSON is malformed");
    }

    [Fact]
    public void Validate_ShouldReportUnknownCapabilitiesOnceAndDuplicatesSeparately()
    {
        var manifest = ParseExample() with { Capabilities = new[] { "bogus", "bogus" } };

        var errors = ProcessorManifestValidator.Validate(manifest).Errors;

        errors.Where(error => error.Contains("'bogus' is not a known capability")).Should().ContainSingle();
        errors.Where(error => error.Contains("'bogus' is declared twice")).Should().ContainSingle();
    }

    [Fact]
    public void Validate_ShouldStillReportHostAndDataClassErrorsWhenNetworkRequiredIsMissing()
    {
        var manifest = ParseExample() with
        {
            Privacy = new ProcessorPrivacyDeclaration(null, new[] { "" }, new[] { "biometric" }, null)
        };

        var errors = ProcessorManifestValidator.Validate(manifest).Errors;

        errors.Should().Contain("privacy.networkRequired: required");
        errors.Should().Contain(error => error.StartsWith("privacy.allowedHosts:"));
        errors.Should().Contain(error => error.Contains("'biometric' is not one of"));
    }

    [Fact]
    public void TryParse_ShouldRejectNumericEnumTokensAndMiscasedMemberNames()
    {
        // The schema fixes kebab-case string enumerations and exact camelCase member names.
        var numeric = WhisperXManifest.Replace("\"execution\": \"sidecar\"", "\"execution\": 1");
        ProcessorManifest.TryParse(numeric, out _, out var numericError).Should().BeFalse();
        numericError.Should().StartWith("Manifest JSON is malformed");

        var miscased = WhisperXManifest.Replace("\"id\": \"taskdeck.whisperx\"", "\"Id\": \"taskdeck.whisperx\"");
        ProcessorManifest.TryParse(miscased, out _, out var miscasedError).Should().BeFalse("'Id' is an unknown member under exact naming");
        miscasedError.Should().StartWith("Manifest JSON is malformed");
    }

    [Fact]
    public void Validate_ShouldRequireNetworkForRemoteLocalityRegardlessOfExecution()
    {
        var manifest = ParseExample() with
        {
            Execution = ProcessorExecutionMode.Sidecar,
            Locality = ProcessorLocality.Remote,
            Privacy = new ProcessorPrivacyDeclaration(false, null, null, null)
        };

        ProcessorManifestValidator.Validate(manifest).Errors
            .Should().Contain(error => error.Contains("compute is remote must declare networkRequired=true"));
    }

    [Fact]
    public void TryParse_ShouldRejectAnUnknownEnumValueAsMalformed()
    {
        var json = WhisperXManifest.Replace("\"sidecar\"", "\"mainframe\"");

        ProcessorManifest.TryParse(json, out _, out var error).Should().BeFalse();
        error.Should().StartWith("Manifest JSON is malformed");
    }
}
