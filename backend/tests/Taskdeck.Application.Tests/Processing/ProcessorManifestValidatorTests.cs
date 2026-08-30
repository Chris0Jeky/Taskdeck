using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.Processing;
using Taskdeck.Application.Processing.Protocol;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Processing;
using Xunit;

namespace Taskdeck.Application.Tests.Processing;

public sealed class ProcessorManifestValidatorTests
{
    /// <summary>
    /// The canonical WhisperX example is read from the assembly's embedded resource — the same bytes
    /// the repository ships beside the schema — so drift between the example and the contract can
    /// no longer be silent (CF-04 residual from PR #2280).
    /// </summary>
    private static readonly string WhisperXManifest = ProcessorManifestResources.ReadWhisperXExample();

    private static ProcessorManifest ParseExample()
    {
        ProcessorManifest.TryParse(WhisperXManifest, out var manifest, out var error).Should().BeTrue(error);
        return manifest!;
    }

    private static IReadOnlyDictionary<string, ProcessorCapabilityContract> ContractsFor(params string[] capabilities) =>
        capabilities.ToDictionary(
            capability => capability,
            capability => new ProcessorCapabilityContract(
                capability == ProcessingCapability.SemanticExtract
                    ? new[] { WorkerProtocol.OutputCandidateBatch, WorkerProtocol.OutputDiagnostic }
                    : new[] { WorkerProtocol.OutputRepresentation },
                new[] { $"https://taskdeck.dev/schemas/{capability}.v1.json" },
                null),
            StringComparer.Ordinal);

    [Fact]
    public void EmbeddedSchema_ShouldBeReadableAndRequireCapabilityContracts()
    {
        using var schema = JsonDocument.Parse(ProcessorManifestResources.ReadSchema());

        schema.RootElement.GetProperty("required").EnumerateArray().Select(element => element.GetString())
            .Should().Contain("capabilityContracts");
        schema.RootElement.GetProperty("properties").TryGetProperty("outputSchemas", out _)
            .Should().BeFalse("output schemas are declared per capability, not globally");
        schema.RootElement.GetProperty("properties").GetProperty("costModel").GetProperty("required")
            .EnumerateArray().Select(element => element.GetString()).Should().Contain("type");
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
        manifest.CapabilityContracts.Should().ContainKeys("audio.transcribe", "audio.align", "audio.diarize");
        manifest.CapabilityContracts!["audio.transcribe"].OptionsSchema.Should().NotBeNullOrEmpty();
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
    public void Validate_ShouldKeepInProcessCapabilitiesOutOfSidecarsAndRemotes()
    {
        var sidecar = ParseExample() with
        {
            Capabilities = new[] { ProcessingCapability.ContextResolve, ProcessingCapability.ChangePlan },
            CapabilityContracts = ContractsFor(ProcessingCapability.ContextResolve, ProcessingCapability.ChangePlan)
        };
        var inProcess = sidecar with
        {
            Execution = ProcessorExecutionMode.InProcess,
            Accepts = new[] { "application/json" }
        };

        var sidecarErrors = ProcessorManifestValidator.Validate(sidecar).Errors;
        sidecarErrors.Should().Contain(error => error.Contains("'context.resolve' stays in-process"));
        sidecarErrors.Should().Contain(error => error.Contains("'change.plan' stays in-process"));

        ProcessorManifestValidator.Validate(inProcess).Errors.Should().NotContain(error => error.Contains("stays in-process"));
        ProcessingCapability.InProcessOnly.Should().BeEquivalentTo(new[] { "context.resolve", "change.plan", "change.verify" });
        ProcessingCapability.Externalizable.Concat(ProcessingCapability.InProcessOnly).Should().BeEquivalentTo(ProcessingCapability.All);
    }

    [Fact]
    public void Validate_ShouldRequireOneContractPerDeclaredCapability()
    {
        var missing = ParseExample() with { CapabilityContracts = ContractsFor("audio.transcribe", "audio.align") };
        var extra = ParseExample() with { CapabilityContracts = ContractsFor("audio.transcribe", "audio.align", "audio.diarize", "image.ocr") };
        var none = ParseExample() with { CapabilityContracts = null };

        ProcessorManifestValidator.Validate(missing).Errors
            .Should().Contain(error => error.Contains("'audio.diarize' is declared without a contract"));
        ProcessorManifestValidator.Validate(extra).Errors
            .Should().Contain(error => error.Contains("capabilityContracts['image.ocr']: not a declared capability"));
        ProcessorManifestValidator.Validate(none).Errors
            .Should().Contain("capabilityContracts: one contract per declared capability is required");
    }

    [Fact]
    public void Validate_ShouldCheckContractOutputFamiliesAndSchemas()
    {
        var contracts = new Dictionary<string, ProcessorCapabilityContract>(StringComparer.Ordinal)
        {
            ["audio.transcribe"] = new(new[] { "diagnostic" }, Array.Empty<string>(), ""),
            ["audio.align"] = new(new[] { "representation", "representation", "blob" }, new[] { "https://taskdeck.dev/schemas/a.json" }, null),
            ["audio.diarize"] = new(new[] { WorkerProtocol.OutputCandidateBatch }, new[] { "https://taskdeck.dev/schemas/a.json" }, null)
        };
        var manifest = ParseExample() with { CapabilityContracts = contracts };

        var errors = ProcessorManifestValidator.Validate(manifest).Errors;

        errors.Should().Contain(error => error.Contains("['audio.transcribe'].outputs") && error.Contains("not diagnostics alone"));
        errors.Should().Contain(error => error.Contains("['audio.transcribe'].outputSchemas"));
        errors.Should().Contain(error => error.Contains("['audio.transcribe'].optionsSchema"));
        errors.Should().Contain(error => error.Contains("['audio.align'].outputs") && error.Contains("'representation' is declared twice"));
        errors.Should().Contain(error => error.Contains("['audio.align'].outputs") && error.Contains("'blob' is not one of"));
        errors.Should().Contain(error => error.Contains("['audio.diarize'].outputs") && error.Contains("only semantic.extract emits candidate batches"));
    }

    [Fact]
    public void Validate_ShouldRequireSemanticExtractToEmitCandidateBatches()
    {
        var manifest = ParseExample() with
        {
            Execution = ProcessorExecutionMode.InProcess,
            Accepts = new[] { "text/plain" },
            Capabilities = new[] { ProcessingCapability.SemanticExtract },
            CapabilityContracts = new Dictionary<string, ProcessorCapabilityContract>(StringComparer.Ordinal)
            {
                [ProcessingCapability.SemanticExtract] = new(new[] { WorkerProtocol.OutputRepresentation }, new[] { "https://taskdeck.dev/schemas/c.json" }, null)
            }
        };

        ProcessorManifestValidator.Validate(manifest).Errors
            .Should().Contain(error => error.Contains("semantic.extract emits candidate batches"));
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
    public void Validate_ShouldRequireACostModelType()
    {
        var manifest = ParseExample() with { CostModel = new ProcessorCostModel(null, "USD", 0) };

        ProcessorManifestValidator.Validate(manifest).Errors
            .Should().Contain(error => error.StartsWith("costModel.type: required when costModel is present"));
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
        json.Should().NotBe(WhisperXManifest);

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

    [Theory]
    [InlineData("\"execution\": 1")]
    [InlineData("\"execution\": \"SIDECAR\"")]
    [InlineData("\"execution\": \"Sidecar\"")]
    [InlineData("\"execution\": \"mainframe\"")]
    public void TryParse_ShouldAcceptOnlyTheExactKebabCaseEnumSpelling(string replacement)
    {
        var json = WhisperXManifest.Replace("\"execution\": \"sidecar\"", replacement);
        json.Should().NotBe(WhisperXManifest);

        ProcessorManifest.TryParse(json, out _, out var error).Should().BeFalse(replacement);
        error.Should().StartWith("Manifest JSON is malformed");
    }

    [Fact]
    public void TryParse_ShouldRejectMiscasedMemberNames()
    {
        var miscased = WhisperXManifest.Replace("\"id\": \"taskdeck.whisperx\"", "\"Id\": \"taskdeck.whisperx\"");

        ProcessorManifest.TryParse(miscased, out _, out var miscasedError).Should().BeFalse("'Id' is an unknown member under exact naming");
        miscasedError.Should().StartWith("Manifest JSON is malformed");
    }

    [Theory]
    [InlineData("InProcess", "in-process")]
    [InlineData("FreeLocal", "free-local")]
    [InlineData("PerMinute", "per-minute")]
    [InlineData("ComputeTime", "compute-time")]
    [InlineData("Sidecar", "sidecar")]
    public void StrictKebabCase_ShouldMatchTheSchemaSpellings(string name, string expected)
    {
        StrictKebabCaseEnumConverterFactory.ToKebabCase(name).Should().Be(expected);
    }

    [Fact]
    public void ManifestJson_ShouldWriteKebabCaseEnumsBack()
    {
        var json = JsonSerializer.Serialize(ParseExample(), ProcessorManifestJson.Options);

        json.Should().Contain("\"execution\":\"sidecar\"");
        json.Should().Contain("\"type\":\"compute-time\"");
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
}
