
using System.Collections.Immutable;

namespace Taskdeck.Acceleration.V06;

public enum ProcessingProfilePreset
{
    Private = 0,
    Balanced = 1,
    Strict = 2,
    Expert = 3
}

public enum ProcessingEgressClass
{
    LocalOnly = 0,
    ApprovedDestinations = 1,
    AnyConfigured = 2
}

public sealed record CapabilityPreference(
    string Capability,
    ImmutableArray<string> OrderedProcessorIds);

public sealed record ProcessingProfile(
    Guid Id,
    int Version,
    string Name,
    ProcessingProfilePreset Preset,
    ProcessingEgressClass EgressClass,
    ImmutableHashSet<string> ApprovedProcessorIds,
    ImmutableHashSet<string> ApprovedHosts,
    ImmutableHashSet<string> ApprovedRegions,
    ImmutableHashSet<string> AllowedDataClasses,
    ImmutableArray<CapabilityPreference> Preferences,
    decimal? PerCaptureCostCeiling,
    long? DeadlineMilliseconds,
    bool AllowGpu,
    bool AllowDiarisation,
    bool AllowAlignment,
    bool AllowOcrEscalation)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Id == Guid.Empty) errors.Add("profile.id.empty");
        if (Version < 1) errors.Add("profile.version.invalid");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("profile.name.empty");
        if (PerCaptureCostCeiling is < 0) errors.Add("profile.cost.negative");
        if (DeadlineMilliseconds is <= 0) errors.Add("profile.deadline.invalid");

        var duplicateCapabilities = Preferences
            .GroupBy(x => x.Capability, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key);
        errors.AddRange(duplicateCapabilities.Select(x => $"profile.preference.duplicate:{x}"));

        foreach (var preference in Preferences)
        {
            if (string.IsNullOrWhiteSpace(preference.Capability))
                errors.Add("profile.preference.capability.empty");
            if (preference.OrderedProcessorIds.IsDefaultOrEmpty)
                errors.Add($"profile.preference.empty:{preference.Capability}");
            if (preference.OrderedProcessorIds.Distinct(StringComparer.Ordinal).Count() !=
                preference.OrderedProcessorIds.Length)
                errors.Add($"profile.preference.processor.duplicate:{preference.Capability}");
        }

        if (EgressClass == ProcessingEgressClass.LocalOnly &&
            (ApprovedHosts.Count > 0 || ApprovedRegions.Count > 0))
            errors.Add("profile.local-only.remote-allowlist");

        return errors;
    }
}
