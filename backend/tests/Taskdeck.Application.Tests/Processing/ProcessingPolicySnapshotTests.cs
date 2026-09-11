using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.Processing.Policy;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Processing;

public sealed class ProcessingPolicySnapshotTests
{
    private const string ExpectedCanonicalJson = """
        {"schemaVersion":1,"egressClass":"approved-destinations","allowedProcessorIds":["provider.cloud-speech","taskdeck.whisperx"],"allowDiarisation":true,"allowAlignment":false,"deadlineUtc":"2026-09-08T12:34:56.1230000Z","costCeiling":{"amount":1.5,"currency":"GBP"}}
        """;

    [Fact]
    public void CanonicalJsonAndDigest_ShouldMatchTheV1GoldenBytes()
    {
        var snapshot = CreateBaseline();

        snapshot.ToCanonicalJson().Should().Be(ExpectedCanonicalJson);
        snapshot.Digest().Should().Be("sha256:2f874cb048ee092d6a78e3075f37fef8254827a0cc1b3e6fa7bdfc80f0d4c9b5");
    }

    [Fact]
    public void Allowlist_ShouldBeOrdinalSortedAndDeduplicatedBeforeHashing()
    {
        var baseline = CreateBaseline();
        var reorderedAndRepeated = new ProcessingPolicySnapshot(
            ProcessingEgressClass.ApprovedDestinations,
            ["taskdeck.whisperx", "provider.cloud-speech", "taskdeck.whisperx"],
            allowDiarisation: true,
            allowAlignment: false,
            deadlineUtc: new DateTimeOffset(2026, 9, 8, 12, 34, 56, 123, TimeSpan.Zero),
            costCeiling: new ProcessingCostCeiling(1.5000m, "GBP"));

        reorderedAndRepeated.AllowedProcessorIds.Should().Equal("provider.cloud-speech", "taskdeck.whisperx");
        reorderedAndRepeated.ToCanonicalJson().Should().Be(baseline.ToCanonicalJson());
        reorderedAndRepeated.Digest().Should().Be(baseline.Digest());
    }

    [Fact]
    public void Constructor_ShouldCopyTheCallerAllowlist()
    {
        var source = new[] { "taskdeck.whisperx" };
        var snapshot = new ProcessingPolicySnapshot(
            ProcessingEgressClass.LocalOnly,
            source,
            allowDiarisation: false,
            allowAlignment: false,
            deadlineUtc: null,
            costCeiling: null);

        source[0] = "provider.cloud-speech";

        snapshot.AllowedProcessorIds.Should().Equal("taskdeck.whisperx");
    }

    [Fact]
    public void CanonicalJson_ShouldWriteExplicitNullLimits()
    {
        var snapshot = new ProcessingPolicySnapshot(
            ProcessingEgressClass.LocalOnly,
            Array.Empty<string>(),
            allowDiarisation: false,
            allowAlignment: false,
            deadlineUtc: null,
            costCeiling: null);

        snapshot.ToCanonicalJson().Should().Be("""
            {"schemaVersion":1,"egressClass":"local-only","allowedProcessorIds":[],"allowDiarisation":false,"allowAlignment":false,"deadlineUtc":null,"costCeiling":null}
            """);
    }

    [Fact]
    public void Digest_ShouldChangeWhenAnyPolicyFieldChanges()
    {
        var baseline = CreateBaseline();
        var variants = new[]
        {
            new ProcessingPolicySnapshot(ProcessingEgressClass.LocalOnly, baseline.AllowedProcessorIds, true, false, baseline.DeadlineUtc, baseline.CostCeiling),
            new ProcessingPolicySnapshot(baseline.EgressClass, ["taskdeck.whisperx"], true, false, baseline.DeadlineUtc, baseline.CostCeiling),
            new ProcessingPolicySnapshot(baseline.EgressClass, baseline.AllowedProcessorIds, false, false, baseline.DeadlineUtc, baseline.CostCeiling),
            new ProcessingPolicySnapshot(baseline.EgressClass, baseline.AllowedProcessorIds, true, true, baseline.DeadlineUtc, baseline.CostCeiling),
            new ProcessingPolicySnapshot(baseline.EgressClass, baseline.AllowedProcessorIds, true, false, baseline.DeadlineUtc!.Value.AddTicks(1), baseline.CostCeiling),
            new ProcessingPolicySnapshot(baseline.EgressClass, baseline.AllowedProcessorIds, true, false, baseline.DeadlineUtc, new ProcessingCostCeiling(1.6m, "GBP"))
        };

        variants.Should().OnlyContain(snapshot => snapshot.Digest() != baseline.Digest());
    }

    [Fact]
    public void CanonicalDigest_ShouldNotDependOnAnAmbientJsonSerializerOptionsInstance()
    {
        var snapshot = CreateBaseline();
        var ambient = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        JsonSerializer.Serialize(snapshot, ambient).Should().NotBe(snapshot.ToCanonicalJson());
        snapshot.Digest().Should().Be(ProcessingPolicySnapshotCanonicalizer.Digest(snapshot));
        snapshot.ToCanonicalJson().Should().Be(ProcessingPolicySnapshotCanonicalizer.Serialize(snapshot));
    }

    [Theory]
    [InlineData("Taskdeck.WhisperX")]
    [InlineData("taskdeck..whisperx")]
    [InlineData(" taskdeck.whisperx")]
    [InlineData("taskdeck.whisperx\n")]
    [InlineData("")]
    public void Constructor_ShouldRejectAmbiguousProcessorIds(string processorId)
    {
        var action = () => new ProcessingPolicySnapshot(
            ProcessingEgressClass.LocalOnly,
            [processorId],
            allowDiarisation: false,
            allowAlignment: false,
            deadlineUtc: null,
            costCeiling: null);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldRejectUndefinedEgressAndNonUtcDeadline()
    {
        var undefinedEgress = () => new ProcessingPolicySnapshot(
            (ProcessingEgressClass)99,
            Array.Empty<string>(),
            false,
            false,
            null,
            null);
        var nonUtcDeadline = () => new ProcessingPolicySnapshot(
            ProcessingEgressClass.LocalOnly,
            Array.Empty<string>(),
            false,
            false,
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(1)),
            null);

        undefinedEgress.Should().Throw<ArgumentOutOfRangeException>();
        nonUtcDeadline.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CostCeiling_ShouldRejectNegativeAmountAndNonCanonicalCurrency()
    {
        var negativeAmount = () => new ProcessingCostCeiling(-0.01m, "GBP");
        var lowerCaseCurrency = () => new ProcessingCostCeiling(1m, "gbp");
        var trailingNewlineCurrency = () => new ProcessingCostCeiling(1m, "GBP\n");

        negativeAmount.Should().Throw<ArgumentOutOfRangeException>();
        lowerCaseCurrency.Should().Throw<ArgumentException>();
        trailingNewlineCurrency.Should().Throw<ArgumentException>();
    }

    private static ProcessingPolicySnapshot CreateBaseline() => new(
        ProcessingEgressClass.ApprovedDestinations,
        ["taskdeck.whisperx", "provider.cloud-speech"],
        allowDiarisation: true,
        allowAlignment: false,
        deadlineUtc: new DateTimeOffset(2026, 9, 8, 12, 34, 56, 123, TimeSpan.Zero),
        costCeiling: new ProcessingCostCeiling(1.5000m, "GBP"));
}
