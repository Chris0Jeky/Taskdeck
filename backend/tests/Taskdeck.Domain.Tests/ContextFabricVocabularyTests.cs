using FluentAssertions;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Domain.Tests;

/// <summary>
/// Pins the v0.6 Context Fabric vocabulary scaffolded ahead of CF-10 <c>#2264</c> and CF-24B <c>#2277</c>
/// (the same way PR <c>#2280</c> landed <see cref="RepresentationKind"/> ahead of CF-06). Persisted
/// receipts and reports will store these values, so member names and numbers are a contract.
/// </summary>
public sealed class ContextFabricVocabularyTests
{
    [Fact]
    public void ProcessingProfilePreset_ShouldBeExactlyTheAdr0065Presets()
    {
        Enum.GetNames<ProcessingProfilePreset>()
            .Should().Equal("Private", "Balanced", "Strict", "Expert");
        ((int)ProcessingProfilePreset.Balanced).Should().Be(1, "Balanced is the fresh-install default (ruling 5)");
        Enum.GetNames<ProcessingProfilePreset>().Should().NotContain("Controlled", "renamed to Strict on 2026-08-30");
    }

    [Fact]
    public void PolicyFamilyVocabularies_ShouldStayVisiblyDistinct()
    {
        // A processing preset must never be confusable with a presentation profile or a workspace mode.
        Enum.GetNames<ProcessingProfilePreset>()
            .Should().NotIntersectWith(new[] { "Flow", "Guided", "Control", "Workbench", "Agent" });
        Enum.GetNames<ProcessingProfilePreset>()
            .Should().NotIntersectWith(new[] { "Observe", "Suggest", "Assist", "Operate", "Autonomous", "Custom" });
    }

    [Fact]
    public void ProcessingEgressClass_ShouldOrderFromMostToLeastRestrictive()
    {
        Enum.GetValues<ProcessingEgressClass>()
            .Should().Equal(
                ProcessingEgressClass.LocalOnly,
                ProcessingEgressClass.ApprovedDestinations,
                ProcessingEgressClass.AnyConfigured);
    }

    [Fact]
    public void ProcessorEligibility_ShouldAccountForEveryCandidateOutcome()
    {
        Enum.GetNames<ProcessorEligibility>()
            .Should().Equal("Chosen", "Eligible", "EligibleNotChosen", "Ineligible");
    }

    [Fact]
    public void ProcessingConsentState_ShouldHaveOneActiveAndThreeTerminalStates()
    {
        Enum.GetNames<ProcessingConsentState>()
            .Should().Equal("Active", "Revoked", "Expired", "Superseded");
        Enum.GetValues<ProcessingConsentState>()
            .Where(state => state != ProcessingConsentState.Active)
            .Should().HaveCount(3);
    }

    [Fact]
    public void MetricAvailability_ShouldNeverReportAbsenceAsZero()
    {
        Enum.GetNames<MetricAvailability>()
            .Should().Equal("Available", "InsufficientCohort", "NoDenominator", "Unknown");
        ((int)MetricAvailability.Available).Should().Be(0);
    }

    [Theory]
    [InlineData(typeof(ProcessingProfilePreset))]
    [InlineData(typeof(ProcessingEgressClass))]
    [InlineData(typeof(ProcessorEligibility))]
    [InlineData(typeof(ProcessingConsentState))]
    [InlineData(typeof(MetricAvailability))]
    public void VocabularyEnums_ShouldBeDenseZeroBasedAndUnique(Type enumType)
    {
        var values = Enum.GetValues(enumType).Cast<int>().OrderBy(value => value).ToArray();
        values.Should().OnlyHaveUniqueItems();
        values.Should().Equal(Enumerable.Range(0, values.Length), "persisted values are a dense zero-based contract");
    }
}
