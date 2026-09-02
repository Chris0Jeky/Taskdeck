using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taskdeck.AccelerationCandidates;

public enum EgressClass { LocalOnly, RemoteAllowedWithConsent, RemoteRequired }
public sealed record ProcessingPolicySnapshot(
    string ContractVersion,
    EgressClass Egress,
    IReadOnlyList<string> AllowedProcessorIds,
    long MaxInputBytes,
    long MaxOutputBytes,
    decimal MaxEstimatedCost,
    DateTimeOffset Deadline,
    bool UserConsentedToRemote)
{
    public string ComputeDigest()
    {
        var canonical = JsonSerializer.Serialize(this, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public void Validate(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ContractVersion)) throw new InvalidOperationException("Contract version required.");
        if (AllowedProcessorIds.Count == 0 || AllowedProcessorIds.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Allowed processors required.");
        if (MaxInputBytes <= 0 || MaxOutputBytes <= 0 || MaxEstimatedCost < 0) throw new InvalidOperationException("Invalid resource limits.");
        if (Deadline <= now) throw new InvalidOperationException("Deadline must be in the future.");
        if (Egress != EgressClass.LocalOnly && !UserConsentedToRemote) throw new InvalidOperationException("Remote processing requires recorded consent.");
    }
}
