
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taskdeck.Acceleration.V06;

public sealed record ProcessingPolicySnapshot(
    int SchemaVersion,
    Guid ProfileId,
    int ProfileVersion,
    ProcessingEgressClass EgressClass,
    ImmutableArray<string> ApprovedProcessorIds,
    ImmutableArray<string> ApprovedHosts,
    ImmutableArray<string> ApprovedRegions,
    ImmutableArray<string> AllowedDataClasses,
    ImmutableArray<CapabilityPreference> Preferences,
    decimal? PerCaptureCostCeiling,
    long? DeadlineMilliseconds,
    bool AllowGpu,
    bool AllowDiarisation,
    bool AllowAlignment,
    bool AllowOcrEscalation)
{
    public string CanonicalJson()
    {
        var canonical = new
        {
            schemaVersion = SchemaVersion,
            profileId = ProfileId,
            profileVersion = ProfileVersion,
            egressClass = EgressClass.ToString(),
            approvedProcessorIds = ApprovedProcessorIds.Order(StringComparer.Ordinal).ToArray(),
            approvedHosts = ApprovedHosts.Select(x => x.ToLowerInvariant()).Order(StringComparer.Ordinal).ToArray(),
            approvedRegions = ApprovedRegions.Order(StringComparer.Ordinal).ToArray(),
            allowedDataClasses = AllowedDataClasses.Order(StringComparer.Ordinal).ToArray(),
            preferences = Preferences.Select(x => new
            {
                capability = x.Capability,
                orderedProcessorIds = x.OrderedProcessorIds.ToArray()
            }).OrderBy(x => x.capability, StringComparer.Ordinal).ToArray(),
            perCaptureCostCeiling = PerCaptureCostCeiling,
            deadlineMilliseconds = DeadlineMilliseconds,
            allowGpu = AllowGpu,
            allowDiarisation = AllowDiarisation,
            allowAlignment = AllowAlignment,
            allowOcrEscalation = AllowOcrEscalation
        };

        return JsonSerializer.Serialize(canonical, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public string Digest()
    {
        var bytes = Encoding.UTF8.GetBytes(CanonicalJson());
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }
}
